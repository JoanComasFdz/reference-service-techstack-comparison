#!/bin/bash
set -euo pipefail

# Refresh NuGet CDN IPs in the firewall ipset.
# NuGet uses Akamai CDN which rotates edge IPs aggressively.
# Run this if 'dotnet restore' fails with connectivity errors.
#
# Usage: sudo bash /workspace/.devcontainer/refresh-nuget-ips.sh

echo "Refreshing NuGet CDN IPs in firewall allowlist..."

nuget_domains="api.nuget.org globalcdn.nuget.org azuresearch-usnc.nuget.org azuresearch-ussc.nuget.org www.nuget.org nuget.org dist.nuget.org"

added=0
for domain in $nuget_domains; do
    echo "Resolving $domain (5 queries)..."
    for i in {1..5}; do
        ips=$(dig +noall +answer A "$domain" | awk '$4 == "A" {print $5}')
        while IFS= read -r ip; do
            if [[ "$ip" =~ ^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$ ]]; then
                ipset add allowed-domains "$ip" -exist 2>/dev/null && added=$((added + 1))
                # Also add /24 subnet
                subnet=$(echo "$ip" | sed 's/\.[0-9]*$/.0\/24/')
                ipset add allowed-domains "$subnet" -exist 2>/dev/null
            fi
        done <<< "$ips"
        sleep 0.3
    done
done

echo "Done. Added/refreshed $added IP entries."

# Verify
if curl --connect-timeout 5 -s https://api.nuget.org/v3/index.json >/dev/null 2>&1; then
    echo "✓ NuGet API is now accessible"
else
    echo "✗ NuGet API still not accessible. The CDN may be using IPs outside known ranges."
    echo "  Check current IPs: dig +short A api.nuget.org"
fi
