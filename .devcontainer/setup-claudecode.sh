#!/bin/bash

# Setup script for Claude Code configuration
# - Auto-configures devcontainer.json if needed
# - Installs Claude Code CLI
# - Pre-caches plugin dependencies (Context7, Serena)
# - Installs Superpowers plugin
# - Configures ccstatusline

set -e  # Exit on error

CLAUDE_DIR="/home/node/.claude"
PLUGINS_DIR="$CLAUDE_DIR/plugins"
SUPERPOWERS_DIR="$PLUGINS_DIR/superpowers"
CCSTATUSLINE_CONFIG_DIR="/home/node/.config/ccstatusline"

echo "========================================="
echo "Setting up Claude Code..."
echo "========================================="

# =========================================
# -1. Auto-configure devcontainer.json
# =========================================
echo ""
echo "Checking devcontainer configuration..."

# Temporarily disable exit-on-error for optional devcontainer configuration
set +e

# Function to find workspace root
find_workspace_root() {
    local current_dir="$PWD"
    while [ "$current_dir" != "/" ]; do
        if [ -d "$current_dir/.git" ] || [ -d "$current_dir/.devcontainer" ] || [ -f "$current_dir/.devcontainer.json" ]; then
            echo "$current_dir"
            return 0
        fi
        current_dir=$(dirname "$current_dir")
    done
    echo "$PWD"  # Fallback to current directory
}

# Function to find devcontainer.json
find_devcontainer_json() {
    local workspace_root="$1"

    if [ -f "$workspace_root/.devcontainer/devcontainer.json" ]; then
        echo "$workspace_root/.devcontainer/devcontainer.json"
    elif [ -f "$workspace_root/.devcontainer.json" ]; then
        echo "$workspace_root/.devcontainer.json"
    else
        echo ""
    fi
}

# Function to check if jq is available
check_jq() {
    if ! command -v jq &> /dev/null; then
        echo "  ⚠ jq not found - skipping devcontainer.json auto-configuration"
        echo "    Install jq to enable automatic devcontainer.json configuration"
        return 1
    fi
    return 0
}

# Function to backup file
backup_file() {
    local file="$1"
    local backup="${file}.backup-$(date +%Y%m%d-%H%M%S)"
    cp "$file" "$backup"
    echo "  → Created backup: $backup"
}

# Function to check if required mounts exist
check_mounts() {
    local devcontainer_json="$1"
    local has_history_mount=$(jq -r '.mounts // [] | map(select(contains("claude-code-bashhistory") and contains("/commandhistory"))) | length > 0' "$devcontainer_json")
    local has_config_mount=$(jq -r '.mounts // [] | map(select(contains("claude-code-config") and contains("/home/node/.claude"))) | length > 0' "$devcontainer_json")

    [ "$has_history_mount" = "true" ] && [ "$has_config_mount" = "true" ]
}

# Function to check if required environment variables exist
check_env_vars() {
    local devcontainer_json="$1"
    local has_claude_dir=$(jq -r '.containerEnv.CLAUDE_CONFIG_DIR // ""' "$devcontainer_json")
    local has_claude_version=$(jq -r '.containerEnv.CLAUDE_CODE_VERSION // ""' "$devcontainer_json")

    [ -n "$has_claude_dir" ] && [ -n "$has_claude_version" ]
}

# Function to add missing mounts
add_mounts() {
    local devcontainer_json="$1"
    local temp_file="${devcontainer_json}.tmp"

    echo "  → Adding Claude Code volume mounts..."
    jq '.mounts = (.mounts // []) + [
        "source=claude-code-bashhistory-${devcontainerId},target=/commandhistory,type=volume",
        "source=claude-code-config-${devcontainerId},target=/home/node/.claude,type=volume"
    ] | .mounts |= unique' "$devcontainer_json" > "$temp_file"

    mv "$temp_file" "$devcontainer_json"
}

# Function to add missing environment variables
add_env_vars() {
    local devcontainer_json="$1"
    local temp_file="${devcontainer_json}.tmp"

    echo "  → Adding Claude Code environment variables..."
    jq '.containerEnv = (.containerEnv // {}) + {
        "CLAUDE_CONFIG_DIR": "/home/node/.claude",
        "CLAUDE_CODE_VERSION": "latest",
        "CONTEXT7_API_KEY": "${localEnv:CONTEXT7_API_KEY}"
    }' "$devcontainer_json" > "$temp_file"

    mv "$temp_file" "$devcontainer_json"
}

# Function to ensure postCreateCommand includes setup script
add_post_create_command() {
    local devcontainer_json="$1"
    local script_path="$2"
    local temp_file="${devcontainer_json}.tmp"

    local current_command=$(jq -r '.postCreateCommand // ""' "$devcontainer_json")

    # Check if setup-claudecode.sh is already in the command
    if [[ "$current_command" == *"setup-claudecode.sh"* ]]; then
        return 0
    fi

    echo "  → Adding setup-claudecode.sh to postCreateCommand..."

    if [ -z "$current_command" ]; then
        # No existing command
        jq ".postCreateCommand = \"bash $script_path\"" "$devcontainer_json" > "$temp_file"
    else
        # Append to existing command
        jq ".postCreateCommand = \"$current_command && bash $script_path\"" "$devcontainer_json" > "$temp_file"
    fi

    mv "$temp_file" "$devcontainer_json"
}

# Main devcontainer configuration logic
WORKSPACE_ROOT=$(find_workspace_root)
DEVCONTAINER_JSON=$(find_devcontainer_json "$WORKSPACE_ROOT")

if [ -z "$DEVCONTAINER_JSON" ]; then
    echo "  ℹ No devcontainer.json found"
    echo "    Claude Code will be installed but devcontainer integration skipped"
    echo "    To enable devcontainer integration, create .devcontainer/devcontainer.json"
elif ! check_jq; then
    echo "  ⚠ Skipping devcontainer.json configuration (jq required)"
else
    echo "  ✓ Found devcontainer.json: $DEVCONTAINER_JSON"

    NEEDS_UPDATE=false

    # Check what's missing
    if ! check_mounts "$DEVCONTAINER_JSON"; then
        echo "  ⚠ Missing Claude Code volume mounts"
        NEEDS_UPDATE=true
    fi

    if ! check_env_vars "$DEVCONTAINER_JSON"; then
        echo "  ⚠ Missing Claude Code environment variables"
        NEEDS_UPDATE=true
    fi

    # Determine script path relative to workspace
    SCRIPT_PATH="/workspace/.devcontainer/setup-claudecode.sh"
    if [ -f "$WORKSPACE_ROOT/.devcontainer/setup-claudecode.sh" ]; then
        SCRIPT_PATH="\${containerWorkspaceFolder}/.devcontainer/setup-claudecode.sh"
    fi

    if [ "$NEEDS_UPDATE" = true ]; then
        echo ""
        echo "  → Updating devcontainer.json with Claude Code infrastructure..."

        # Wrap in error handling to not break Claude Code installation
        if backup_file "$DEVCONTAINER_JSON" 2>/dev/null; then
            UPDATE_SUCCESS=true

            if ! check_mounts "$DEVCONTAINER_JSON"; then
                add_mounts "$DEVCONTAINER_JSON" || UPDATE_SUCCESS=false
            fi

            if ! check_env_vars "$DEVCONTAINER_JSON"; then
                add_env_vars "$DEVCONTAINER_JSON" || UPDATE_SUCCESS=false
            fi

            add_post_create_command "$DEVCONTAINER_JSON" "$SCRIPT_PATH" || UPDATE_SUCCESS=false

            if [ "$UPDATE_SUCCESS" = true ]; then
                echo "  ✓ devcontainer.json configured for Claude Code"
                echo "    Note: You'll need to rebuild the container for changes to take effect"
            else
                echo "  ✗ Failed to update devcontainer.json"
                echo "    Continuing with Claude Code installation anyway..."
            fi
        else
            echo "  ✗ Cannot backup devcontainer.json (permission denied?)"
            echo "    Skipping devcontainer.json update, continuing with installation..."
        fi
    else
        echo "  ✓ devcontainer.json already configured for Claude Code"
    fi
fi

# Re-enable exit-on-error for Claude Code installation
set -e

# =========================================
# 0. Install Claude Code CLI
# =========================================
echo ""
echo "Checking Claude Code installation..."

# Get the Claude Code version from build arg or default to latest
CLAUDE_CODE_VERSION="${CLAUDE_CODE_VERSION:-latest}"

# Check if Claude Code is already installed
if command -v claude &> /dev/null; then
    INSTALLED_VERSION=$(claude --version 2>/dev/null | head -1 || echo "unknown")
    echo "  ✓ Claude Code already installed ($INSTALLED_VERSION)"
else
    echo "  → Installing Claude Code CLI (version: $CLAUDE_CODE_VERSION)..."
    npm install -g "@anthropic-ai/claude-code@${CLAUDE_CODE_VERSION}"

    if [ $? -eq 0 ]; then
        echo "  ✓ Claude Code installed successfully!"
    else
        echo "  ✗ Failed to install Claude Code"
        echo "    You can install manually with: npm install -g @anthropic-ai/claude-code"
        exit 1
    fi
fi

# =========================================
# 0.1. Pre-install Plugin Dependencies
# =========================================
echo ""
echo "Pre-installing plugin dependencies..."

# Note: Context7 and Serena are installed as plugins via the official marketplace
# Pre-cache Serena dependencies (Python-based with uvx) for faster first-run
echo "  → Pre-caching Serena plugin dependencies..."
uvx --from git+https://github.com/oraios/serena serena --help >/dev/null 2>&1 || true
if [ $? -eq 0 ]; then
    echo "  ✓ Serena plugin dependencies cached"
else
    echo "  ⚠ Failed to pre-cache Serena (will be installed on first use)"
fi

# =========================================
# 1. Install Superpowers Plugin
# =========================================
echo ""
echo "Setting up Claude Code Superpowers plugin..."

# Create plugins directory if it doesn't exist
mkdir -p "$PLUGINS_DIR"

# Check if already installed
if [ -d "$SUPERPOWERS_DIR" ]; then
    echo "✓ Superpowers plugin already installed"
    # Update it
    cd "$SUPERPOWERS_DIR" && git pull --quiet 2>/dev/null || echo "  (could not update, but plugin is installed)"
else
    echo "Installing Superpowers plugin..."
    # Clone the plugin repository
    git clone --quiet https://github.com/obra/superpowers.git "$SUPERPOWERS_DIR" 2>/dev/null

    if [ $? -eq 0 ]; then
        echo "✓ Superpowers plugin installed successfully!"
        echo "  Skills and commands are now available in Claude Code"
    else
        echo "✗ Failed to install Superpowers plugin"
        echo "  You can install manually with:"
        echo "    /plugin marketplace add obra/superpowers-marketplace"
        echo "    /plugin install superpowers@superpowers-marketplace"
    fi
fi

# =========================================
# 2. MCP Servers (via Plugins)
# =========================================
echo ""
echo "MCP servers configuration..."
echo "  ℹ Context7 and Serena are available as plugins from the official marketplace"
echo "  ℹ Plugins are configured in settings.json and auto-installed on first use"

# =========================================
# Fix installMethod detection issue
# =========================================
# Claude Code's detection logic doesn't recognize custom NPM_CONFIG_PREFIX paths
# This causes installMethod to be set to "unknown" on first run
# We fix it post-initialization since the runtime detection works correctly
if [ -f "$CLAUDE_DIR/.claude.json" ]; then
    CURRENT_METHOD=$(jq -r '.installMethod // "missing"' "$CLAUDE_DIR/.claude.json" 2>/dev/null)
    if [ "$CURRENT_METHOD" = "unknown" ] || [ "$CURRENT_METHOD" = "missing" ]; then
        echo ""
        echo "Fixing installMethod detection..."
        jq '.installMethod = "npm-global"' "$CLAUDE_DIR/.claude.json" > "$CLAUDE_DIR/.claude.json.tmp" 2>/dev/null
        mv "$CLAUDE_DIR/.claude.json.tmp" "$CLAUDE_DIR/.claude.json"
        echo "  ✓ Set installMethod to npm-global (workaround for custom npm prefix)"
    fi
fi

# =========================================
# Configure ccstatusline
# =========================================
echo ""
echo "Configuring ccstatusline..."

# Create ccstatusline config directory
mkdir -p "$CCSTATUSLINE_CONFIG_DIR"

# Copy pre-configured settings if they exist
if [ -f "/workspace/.devcontainer/ccstatusline.settings.json" ]; then
    cp "/workspace/.devcontainer/ccstatusline.settings.json" "$CCSTATUSLINE_CONFIG_DIR/settings.json"
    echo "  ✓ Copied ccstatusline configuration"
else
    echo "  ⚠ ccstatusline config not found at /workspace/.devcontainer/ccstatusline.settings.json"
fi

# Add statusLine setting to Claude Code config
if [ -f "$CLAUDE_DIR/.claude.json" ]; then
    # Check if statusLine setting already exists
    HAS_STATUSLINE=$(jq 'has("statusLine")' "$CLAUDE_DIR/.claude.json" 2>/dev/null)
    if [ "$HAS_STATUSLINE" = "false" ]; then
        echo "  → Adding statusLine configuration to Claude Code..."
        jq '.statusLine = "npx ccstatusline@latest"' "$CLAUDE_DIR/.claude.json" > "$CLAUDE_DIR/.claude.json.tmp" 2>/dev/null
        mv "$CLAUDE_DIR/.claude.json.tmp" "$CLAUDE_DIR/.claude.json"
        echo "  ✓ ccstatusline enabled in Claude Code"
    else
        echo "  ✓ statusLine already configured in Claude Code"
    fi
else
    echo "  ℹ Claude config doesn't exist yet (will be created on first run)"
    echo "    statusLine will be configured in settings.json instead"
fi

# Add statusLine to settings.json with correct object format
# This works even on fresh installs before .claude.json exists
if [ -f "$CLAUDE_DIR/settings.json" ]; then
    HAS_STATUSLINE_SETTINGS=$(jq 'has("statusLine")' "$CLAUDE_DIR/settings.json" 2>/dev/null)
    if [ "$HAS_STATUSLINE_SETTINGS" = "false" ]; then
        echo "  → Adding statusLine to settings.json with object format..."
        jq '.statusLine = {"type": "command", "command": "npx ccstatusline@latest", "padding": 0}' "$CLAUDE_DIR/settings.json" > "$CLAUDE_DIR/settings.json.tmp" 2>/dev/null
        mv "$CLAUDE_DIR/settings.json.tmp" "$CLAUDE_DIR/settings.json"
        echo "  ✓ statusLine added to settings.json"
    else
        echo "  ✓ statusLine already configured in settings.json"
    fi
else
    # Create settings.json with statusLine on fresh install
    echo "  → Creating settings.json with statusLine configuration..."
    echo '{"statusLine": {"type": "command", "command": "npx ccstatusline@latest", "padding": 0}}' | jq '.' > "$CLAUDE_DIR/settings.json" 2>/dev/null
    echo "  ✓ settings.json created with statusLine"
fi

# Set proper ownership for ccstatusline config
chown -R node:node "$CCSTATUSLINE_CONFIG_DIR" 2>/dev/null || true

# =========================================
# Set proper ownership
# =========================================
chown -R node:node "$CLAUDE_DIR" 2>/dev/null || true

echo ""
echo "========================================="
echo "✓ Claude Code setup complete!"
echo "========================================="
echo ""
