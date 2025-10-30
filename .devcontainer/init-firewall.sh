#!/bin/bash
set -euo pipefail  # Exit on error, undefined vars, and pipeline failures
IFS=$'\n\t'       # Stricter word splitting

# IMPORTANT: This firewall script must work alongside Docker's iptables rules
# We only flush and manage INPUT/OUTPUT chains, leaving FORWARD and nat table for Docker

echo "Setting up firewall rules (preserving Docker networking)..."

# Flush only INPUT and OUTPUT chains (leave FORWARD and nat for Docker)
iptables -F INPUT 2>/dev/null || true
iptables -F OUTPUT 2>/dev/null || true

# Delete old custom chains (but not Docker chains)
for chain in $(iptables -L -n | grep "^Chain" | grep -v "INPUT\|OUTPUT\|FORWARD\|DOCKER" | awk '{print $2}'); do
    iptables -F "$chain" 2>/dev/null || true
    iptables -X "$chain" 2>/dev/null || true
done

# Delete custom ipsets
ipset destroy allowed-domains 2>/dev/null || true

# First allow DNS and localhost before any restrictions
# Allow outbound DNS
iptables -A OUTPUT -p udp --dport 53 -j ACCEPT
# Allow inbound DNS responses
iptables -A INPUT -p udp --sport 53 -j ACCEPT
# Allow outbound SSH
iptables -A OUTPUT -p tcp --dport 22 -j ACCEPT
# Allow inbound SSH responses
iptables -A INPUT -p tcp --sport 22 -m state --state ESTABLISHED -j ACCEPT
# Allow localhost
iptables -A INPUT -i lo -j ACCEPT
iptables -A OUTPUT -o lo -j ACCEPT

# Create ipset with CIDR support
ipset create allowed-domains hash:net

# Fetch GitHub meta information and aggregate + add their IP ranges
echo "Fetching GitHub IP ranges..."
gh_ranges=$(curl -s https://api.github.com/meta)
if [ -z "$gh_ranges" ]; then
    echo "ERROR: Failed to fetch GitHub IP ranges"
    exit 1
fi

if ! echo "$gh_ranges" | jq -e '.web and .api and .git' >/dev/null; then
    echo "ERROR: GitHub API response missing required fields"
    exit 1
fi

echo "Processing GitHub IPs..."
while read -r cidr; do
    if [[ ! "$cidr" =~ ^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}/[0-9]{1,2}$ ]]; then
        echo "ERROR: Invalid CIDR range from GitHub meta: $cidr"
        exit 1
    fi
    echo "Adding GitHub range $cidr"
    ipset add allowed-domains "$cidr" -exist
done < <(echo "$gh_ranges" | jq -r '(.web + .api + .git)[]' | aggregate -q)

# Resolve and add other allowed domains
for domain in \
    "github.com" \
    "registry.npmjs.org" \
    "api.anthropic.com" \
    "sentry.io" \
    "statsig.anthropic.com" \
    "statsig.com" \
    "marketplace.visualstudio.com" \
    "vscode.blob.core.windows.net" \
    "update.code.visualstudio.com" \
    "docs.microsoft.com" \
    "learn.microsoft.com" \
    "download.visualstudio.microsoft.com" \
    "download.microsoft.com" \
    "az764295.vo.msecnd.net" \
    "vscode-download.azureedge.net" \
    "vscodeextensiongallery.blob.core.windows.net" \
    "vscodeextensions.blob.core.windows.net" \
    "vscodehub.azureedge.net" \
    "vsassets.io" \
    "vsmarketplacebadges.dev" \
    "api.nuget.org" \
    "www.nuget.org" \
    "nuget.org" \
    "globalcdn.nuget.org" \
    "context7.com" \
    "mcp.context7.com" \
    "pypi.org" \
    "files.pythonhosted.org" \
    "pypi.python.org" \
    "registry-1.docker.io" \
    "auth.docker.io" \
    "registry.hub.docker.com" \
    "production.cloudflare.docker.com" \
    "index.docker.io"; do
    echo "Resolving $domain..."
    ips=$(dig +noall +answer A "$domain" | awk '$4 == "A" {print $5}')
    if [ -z "$ips" ]; then
        echo "WARNING: Failed to resolve $domain, skipping..."
        continue
    fi

    while read -r ip; do
        if [[ ! "$ip" =~ ^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$ ]]; then
            echo "WARNING: Invalid IP from DNS for $domain: $ip, skipping..."
            continue
        fi
        echo "Adding $ip for $domain"
        ipset add allowed-domains "$ip" -exist
    done < <(echo "$ips")
done

# Add known IPs that may not resolve via DNS (Azure CDN, Microsoft services)
echo "Adding known Microsoft/Azure CDN IPs..."
for ip in \
    "57.150.149.97"; do
    echo "Adding direct IP: $ip"
    ipset add allowed-domains "$ip" -exist
done

# Add Cloudflare IP ranges (used by Docker Hub CDN)
echo "Adding Cloudflare IP ranges for Docker Hub..."
cloudflare_ranges=$(curl -s https://www.cloudflare.com/ips-v4)
if [ -n "$cloudflare_ranges" ]; then
    while read -r cidr; do
        if [[ "$cidr" =~ ^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}/[0-9]{1,2}$ ]]; then
            echo "Adding Cloudflare range $cidr"
            ipset add allowed-domains "$cidr" -exist
        fi
    done < <(echo "$cloudflare_ranges")
else
    echo "WARNING: Failed to fetch Cloudflare IP ranges, Docker Hub may not work"
fi

# Get host IP from default route
HOST_IP=$(ip route | grep default | cut -d" " -f3)
if [ -z "$HOST_IP" ]; then
    echo "ERROR: Failed to detect host IP"
    exit 1
fi

HOST_NETWORK=$(echo "$HOST_IP" | sed "s/\.[0-9]*$/.0\/24/")
echo "Host network detected as: $HOST_NETWORK"

# Set up remaining iptables rules
iptables -A INPUT -s "$HOST_NETWORK" -j ACCEPT
iptables -A OUTPUT -d "$HOST_NETWORK" -j ACCEPT

# Allow Docker bridge network traffic (for docker-in-docker)
# Note: We insert these rules at the top to ensure they're evaluated first
iptables -I INPUT 1 -i docker0 -j ACCEPT
iptables -I OUTPUT 1 -o docker0 -j ACCEPT
# Also allow traffic on any docker-created bridge networks (br-*)
iptables -I INPUT 1 -i br-+ -j ACCEPT
iptables -I OUTPUT 1 -o br-+ -j ACCEPT

# Set default policies to DROP (but leave FORWARD alone for Docker to manage)
iptables -P INPUT DROP
# Do NOT set FORWARD policy - Docker manages this
# iptables -P FORWARD DROP
iptables -P OUTPUT DROP

# First allow established connections for already approved traffic
iptables -I INPUT 1 -m state --state ESTABLISHED,RELATED -j ACCEPT
iptables -I OUTPUT 1 -m state --state ESTABLISHED,RELATED -j ACCEPT

# Then allow only specific outbound traffic to allowed domains
iptables -A OUTPUT -m set --match-set allowed-domains dst -j ACCEPT

# Explicitly REJECT all other outbound traffic for immediate feedback
iptables -A OUTPUT -j REJECT --reject-with icmp-admin-prohibited

echo "Firewall configuration complete"
echo "Verifying firewall rules..."
if curl --connect-timeout 5 https://example.com >/dev/null 2>&1; then
    echo "ERROR: Firewall verification failed - was able to reach https://example.com"
    exit 1
else
    echo "Firewall verification passed - unable to reach https://example.com as expected"
fi

# Verify GitHub API access
if ! curl --connect-timeout 5 https://api.github.com/zen >/dev/null 2>&1; then
    echo "ERROR: Firewall verification failed - unable to reach https://api.github.com"
    exit 1
else
    echo "Firewall verification passed - able to reach https://api.github.com as expected"
fi
