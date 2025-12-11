# Devcontainer Migration Implementation Plan

## Overview

This plan migrates the devcontainer to use features incrementally, with verification at each step. Each phase can be reverted independently if issues arise.

---

## Phase 1: Baseline Verification

**Goal:** Establish current working state and create rollback point

### Step 1.1: Document Current State
```bash
# From inside current devcontainer, capture versions
node --version > /tmp/baseline-versions.txt
dotnet --version >> /tmp/baseline-versions.txt
python3 --version >> /tmp/baseline-versions.txt
gh --version >> /tmp/baseline-versions.txt
docker --version >> /tmp/baseline-versions.txt
k6 version >> /tmp/baseline-versions.txt
zsh --version >> /tmp/baseline-versions.txt
git-delta --version >> /tmp/baseline-versions.txt 2>/dev/null || delta --version >> /tmp/baseline-versions.txt
uv --version >> /tmp/baseline-versions.txt
cat /tmp/baseline-versions.txt
```

### Step 1.2: Create Backup Branch
```bash
cd /workspace
git checkout -b devcontainer-feature-migration
git add .devcontainer/
git commit -m "backup: current devcontainer before feature migration"
```

**Verification:** Branch created, all files committed

---

## Phase 2: Add Features Infrastructure (No Dockerfile Changes Yet)

**Goal:** Add features alongside existing Dockerfile to verify features work

### Step 2.1: Add Empty Features Block
Edit `.devcontainer/devcontainer.json`:
```json
{
  "name": "reference-service-techstack-comparison",
  "build": {
    "dockerfile": "Dockerfile",
    "args": {
      "TZ": "${localEnv:TZ:America/Los_Angeles}",
      "GIT_DELTA_VERSION": "0.18.2",
      "ZSH_IN_DOCKER_VERSION": "1.2.0"
    }
  },
  "features": {},
  ... rest unchanged ...
}
```

**Verification:**
```bash
# Rebuild container (from VS Code: "Dev Containers: Rebuild Container")
# Should work exactly as before
node --version  # Should match baseline
```

---

## Phase 3: Migrate GitHub CLI (Lowest Risk)

**Goal:** Replace apt-get gh with feature

### Step 3.1: Add GitHub CLI Feature
Edit `.devcontainer/devcontainer.json`:
```json
"features": {
  "ghcr.io/devcontainers/features/github-cli:1": {
    "installDirectlyFromGitHubRelease": true,
    "version": "latest"
  }
}
```

### Step 3.2: Remove gh from Dockerfile
Edit `.devcontainer/Dockerfile`, remove `gh \` from apt-get line:
```dockerfile
RUN apt-get update && apt-get install -y --no-install-recommends \
  less \
  git \
  procps \
  sudo \
  fzf \
  zsh \
  man-db \
  unzip \
  gnupg2 \
  # gh \  <-- REMOVE THIS LINE
  iptables \
  ...
```

**Verification:**
```bash
# Rebuild container
gh --version
gh auth status  # Should still be configured if was before
```

**Rollback if fails:** Revert Dockerfile change, remove feature

---

## Phase 4: Migrate Git Delta

**Goal:** Replace manual deb install with feature

### Step 4.1: Add Delta Feature
Edit `.devcontainer/devcontainer.json`:
```json
"features": {
  "ghcr.io/devcontainers/features/github-cli:1": { ... },
  "ghcr.io/eitsupi/devcontainer-features/delta:0": {
    "version": "latest"
  }
}
```

### Step 4.2: Remove Delta from Dockerfile
Remove these lines from `.devcontainer/Dockerfile`:
```dockerfile
# REMOVE THIS BLOCK:
ARG GIT_DELTA_VERSION=0.18.2
RUN ARCH=$(dpkg --print-architecture) && \
  wget "https://github.com/dandavison/delta/releases/download/${GIT_DELTA_VERSION}/git-delta_${GIT_DELTA_VERSION}_${ARCH}.deb" && \
  sudo dpkg -i "git-delta_${GIT_DELTA_VERSION}_${ARCH}.deb" && \
  rm "git-delta_${GIT_DELTA_VERSION}_${ARCH}.deb"
```

Also remove from build args in devcontainer.json:
```json
"args": {
  "TZ": "${localEnv:TZ:America/Los_Angeles}",
  // "GIT_DELTA_VERSION": "0.18.2",  <-- REMOVE
  "ZSH_IN_DOCKER_VERSION": "1.2.0"
}
```

**Verification:**
```bash
# Rebuild container
delta --version  # or git-delta --version
git diff --color-words HEAD~1  # Should show colored diff with delta
```

**Rollback if fails:** Restore Dockerfile block, remove feature

---

## Phase 5: Migrate .NET SDK (Medium Risk)

**Goal:** Replace manual .NET installation with feature

### Step 5.1: Add .NET Feature
Edit `.devcontainer/devcontainer.json`:
```json
"features": {
  "ghcr.io/devcontainers/features/github-cli:1": { ... },
  "ghcr.io/eitsupi/devcontainer-features/delta:0": { ... },
  "ghcr.io/devcontainers/features/dotnet:2": {
    "version": "9.0",
    "installUsingApt": true
  }
}
```

### Step 5.2: Remove .NET from Dockerfile
Remove these lines:
```dockerfile
# REMOVE THIS BLOCK:
# Install .NET 9 SDK
RUN wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
  dpkg -i packages-microsoft-prod.deb && \
  rm packages-microsoft-prod.deb && \
  apt-get update && \
  apt-get install -y dotnet-sdk-9.0 && \
  apt-get clean && rm -rf /var/lib/apt/lists/*
```

**Verification:**
```bash
# Rebuild container
dotnet --version  # Should be 9.x
dotnet --list-sdks  # Should show 9.0.x

# Build the project
cd /workspace/performance-tester-dotnet
dotnet build

# Run tests (subset)
dotnet test src/PerformanceTester.IntegrationTesting.Tests --filter "FullyQualifiedName~ContainerManager"
```

**Rollback if fails:** Restore Dockerfile block, remove feature

---

## Phase 6: Migrate Python + uv (Medium Risk)

**Goal:** Replace apt Python + COPY uv with features

### Step 6.1: Add Python Feature
Edit `.devcontainer/devcontainer.json`:
```json
"features": {
  ... existing features ...,
  "ghcr.io/devcontainers/features/python:1": {
    "version": "3.12",
    "installTools": true
  }
}
```

Note: Using 3.12 as it's more stable with features. 3.13 may not be available.

### Step 6.2: Add uv Feature
```json
"features": {
  ... existing features ...,
  "ghcr.io/devcontainers/features/python:1": { ... },
  "ghcr.io/astral-sh/uv-devcontainer-feature/uv:latest": {}
}
```

### Step 6.3: Remove from Dockerfile
Remove these lines:
```dockerfile
# REMOVE from apt-get:
  python3 \
  python3-pip \
  python3-venv \

# REMOVE this line:
COPY --from=ghcr.io/astral-sh/uv:latest /uv /uvx /usr/local/bin/
```

**Verification:**
```bash
# Rebuild container
python3 --version  # Should be 3.12.x
uv --version
uvx --version

# Test MCP server can be invoked
uvx --from git+https://github.com/oraios/serena serena-mcp-server --help
```

**Rollback if fails:** Restore Dockerfile lines, remove features

---

## Phase 7: Migrate Docker CLI (Higher Risk)

**Goal:** Replace apt docker.io with feature

### Step 7.1: Add Docker Feature
```json
"features": {
  ... existing features ...,
  "ghcr.io/devcontainers/features/docker-outside-of-docker:1": {
    "moby": false,
    "installDockerBuildx": true,
    "installDockerComposeSwitch": true
  }
}
```

Note: `moby: false` because we're using host Docker socket.

### Step 7.2: Remove from Dockerfile
Remove these lines:
```dockerfile
# REMOVE THIS BLOCK:
# Install Docker CLI and docker-compose for socket-based Docker access
RUN apt-get update && apt-get install -y --no-install-recommends docker.io docker-compose && \
    apt-get clean && rm -rf /var/lib/apt/lists/*
```

**KEEP** the docker group configuration:
```dockerfile
# KEEP THIS - needed for WSL2 socket access:
RUN groupmod -g 1001 docker 2>/dev/null || groupadd -g 1001 docker && \
    usermod -aG docker node
```

**Verification:**
```bash
# Rebuild container
docker --version
docker-compose --version  # or docker compose version
docker ps  # Should connect to host Docker

# Test Testcontainers work
cd /workspace/performance-tester-dotnet
dotnet test src/PerformanceTester.IntegrationTesting.Tests
```

**Rollback if fails:** Restore Dockerfile block, remove feature

---

## Phase 8: Migrate Zsh Setup (Medium Risk)

**Goal:** Replace zsh-in-docker script with common-utils feature

### Step 8.1: Add common-utils Feature
```json
"features": {
  ... existing features ...,
  "ghcr.io/devcontainers/features/common-utils:2": {
    "installZsh": true,
    "configureZshAsDefaultShell": true,
    "installOhMyZsh": true,
    "installOhMyZshConfig": true,
    "username": "node",
    "upgradePackages": false
  }
}
```

### Step 8.2: Remove zsh-in-docker from Dockerfile
Remove these lines:
```dockerfile
# REMOVE THIS BLOCK:
# Default powerline10k theme
ARG ZSH_IN_DOCKER_VERSION=1.2.0
RUN sh -c "$(wget -O- https://github.com/deluan/zsh-in-docker/releases/download/v${ZSH_IN_DOCKER_VERSION}/zsh-in-docker.sh)" -- \
  -p git \
  -p fzf \
  -a "source /usr/share/doc/fzf/examples/key-bindings.zsh" \
  -a "source /usr/share/doc/fzf/examples/completion.zsh" \
  -a "export PROMPT_COMMAND='history -a' && export HISTFILE=/commandhistory/.bash_history" \
  -x
```

Also remove from build args:
```json
"args": {
  "TZ": "${localEnv:TZ:America/Los_Angeles}"
  // "ZSH_IN_DOCKER_VERSION": "1.2.0"  <-- REMOVE
}
```

### Step 8.3: Update postcreate-wrapper.sh for fzf
Add fzf sourcing to postcreate since common-utils doesn't do it:
```bash
# In postcreate-wrapper.sh, add after Step 5:
log "Step 6: Configuring fzf for zsh..."
if [ -f /usr/share/doc/fzf/examples/key-bindings.zsh ]; then
    echo 'source /usr/share/doc/fzf/examples/key-bindings.zsh' >> /home/node/.zshrc
    echo 'source /usr/share/doc/fzf/examples/completion.zsh' >> /home/node/.zshrc
    log "✓ fzf configured for zsh"
else
    log "⚠ fzf zsh files not found, skipping"
fi
```

**Verification:**
```bash
# Rebuild container
echo $SHELL  # Should be /bin/zsh or /usr/bin/zsh
zsh --version

# Test fzf works
# Press Ctrl+R in terminal - should show fuzzy history search
# Press Ctrl+T - should show fuzzy file finder
```

**Rollback if fails:** Restore Dockerfile block, remove feature, revert postcreate

---

## Phase 9: Clean Up Remaining Dockerfile

**Goal:** Remove redundant apt packages now handled by features

### Step 9.1: Audit Remaining apt-get Packages

After all migrations, the apt-get should only contain:
```dockerfile
RUN apt-get update && apt-get install -y --no-install-recommends \
  # Kept by features: less, git, procps, sudo, zsh, man-db, unzip, gnupg2, curl
  # Must keep for firewall:
  iptables \
  ipset \
  iproute2 \
  iputils-ping \
  dnsutils \
  aggregate \
  netcat-openbsd \
  # Optional tools (keep or remove):
  fzf \
  jq \
  nano \
  vim \
  && apt-get clean && rm -rf /var/lib/apt/lists/*
```

### Step 9.2: Create Minimal Dockerfile
Create new simplified Dockerfile:

```dockerfile
FROM node:20

ARG TZ
ENV TZ="$TZ"

# Upgrade npm
RUN npm install -g npm@latest

# Install ONLY what features can't provide:
# - Firewall tools (iptables, ipset, aggregate)
# - Network diagnostics (for debugging)
# - jq (for firewall script)
RUN apt-get update && apt-get install -y --no-install-recommends \
  iptables \
  ipset \
  iproute2 \
  iputils-ping \
  dnsutils \
  aggregate \
  netcat-openbsd \
  jq \
  fzf \
  && apt-get clean && rm -rf /var/lib/apt/lists/*

# Ensure default node user has access to /usr/local/share
RUN mkdir -p /usr/local/share/npm-global && \
  chown -R node:node /usr/local/share

ARG USERNAME=node

# Persist bash history
RUN SNIPPET="export PROMPT_COMMAND='history -a' && export HISTFILE=/commandhistory/.bash_history" \
  && mkdir /commandhistory \
  && touch /commandhistory/.bash_history \
  && chown -R $USERNAME /commandhistory

ENV DEVCONTAINER=true

# Create workspace and config directories
RUN mkdir -p /workspace /home/node/.claude && \
  chown -R node:node /workspace /home/node/.claude

WORKDIR /workspace

# Set up non-root user
USER node

ENV NPM_CONFIG_PREFIX=/usr/local/share/npm-global
ENV PATH=$PATH:/usr/local/share/npm-global/bin
ENV SHELL=/bin/zsh
ENV EDITOR=nano
ENV VISUAL=nano

# Firewall scripts and sudoers
COPY init-firewall.sh /usr/local/bin/
COPY fix-docker-iptables.sh /usr/local/bin/
USER root
RUN chmod +x /usr/local/bin/init-firewall.sh /usr/local/bin/fix-docker-iptables.sh && \
  echo "node ALL=(root) NOPASSWD: /usr/local/bin/init-firewall.sh" > /etc/sudoers.d/node-firewall && \
  echo "node ALL=(root) NOPASSWD: /usr/local/bin/fix-docker-iptables.sh" >> /etc/sudoers.d/node-firewall && \
  echo "node ALL=(root) NOPASSWD: /usr/bin/pkill *" >> /etc/sudoers.d/node-firewall && \
  echo "node ALL=(root) NOPASSWD: /bin/chmod 666 /var/run/docker.sock" >> /etc/sudoers.d/node-firewall && \
  chmod 0440 /etc/sudoers.d/node-firewall

# Docker group for WSL2 socket compatibility
RUN groupmod -g 1001 docker 2>/dev/null || groupadd -g 1001 docker && \
    usermod -aG docker node

USER node
```

**Verification:**
```bash
# Rebuild container

# Test all tools
node --version
dotnet --version
python3 --version
gh --version
docker --version
k6 version
delta --version
uv --version
zsh --version

# Test firewall
sudo /usr/local/bin/init-firewall.sh
curl https://api.github.com/zen  # Should work
curl https://example.com  # Should fail (blocked)

# Test Docker/Testcontainers
docker ps
cd /workspace/performance-tester-dotnet
dotnet test src/PerformanceTester.IntegrationTesting.Tests

# Test Claude Code
claude --version
```

---

## Phase 10: Final Validation

**Goal:** Full end-to-end verification

### Step 10.1: Complete Test Suite
```bash
cd /workspace/performance-tester-dotnet

# Build everything
dotnet build

# Run all integration tests
dotnet test

# Verify all test projects pass
dotnet test src/PerformanceTester.IntegrationTesting.Tests
dotnet test src/PerformanceTester.Infrastructure.IntegrationTests
dotnet test src/PerformanceTester.EventPublishing.IntegrationTests
dotnet test src/PerformanceTester.EventConsuming.IntegrationTests
# ... etc
```

### Step 10.2: Test Claude Code Integration
```bash
# Verify MCP servers work
claude mcp list

# Verify Claude Code can start
claude --help
```

### Step 10.3: Test Firewall
```bash
# Allowed domains
curl -I https://github.com
curl -I https://api.nuget.org
curl -I https://registry.npmjs.org
curl -I https://api.anthropic.com

# Blocked domains (should fail)
curl --connect-timeout 5 https://example.com && echo "FAIL: Should be blocked" || echo "PASS: Blocked"
curl --connect-timeout 5 https://google.com && echo "FAIL: Should be blocked" || echo "PASS: Blocked"
```

### Step 10.4: Compare Versions to Baseline
```bash
# Generate new versions file
node --version > /tmp/new-versions.txt
dotnet --version >> /tmp/new-versions.txt
python3 --version >> /tmp/new-versions.txt
gh --version >> /tmp/new-versions.txt
docker --version >> /tmp/new-versions.txt
k6 version >> /tmp/new-versions.txt
zsh --version >> /tmp/new-versions.txt
delta --version >> /tmp/new-versions.txt
uv --version >> /tmp/new-versions.txt

# Compare (versions may differ but all should be present)
diff /tmp/baseline-versions.txt /tmp/new-versions.txt || echo "Versions changed (expected)"
```

---

## Final devcontainer.json

```json
{
  "name": "reference-service-techstack-comparison",
  "build": {
    "dockerfile": "Dockerfile",
    "args": {
      "TZ": "${localEnv:TZ:America/Los_Angeles}"
    }
  },
  "features": {
    "ghcr.io/devcontainers/features/common-utils:2": {
      "installZsh": true,
      "configureZshAsDefaultShell": true,
      "installOhMyZsh": true,
      "username": "node",
      "upgradePackages": false
    },
    "ghcr.io/devcontainers/features/github-cli:1": {
      "installDirectlyFromGitHubRelease": true,
      "version": "latest"
    },
    "ghcr.io/devcontainers/features/dotnet:2": {
      "version": "9.0",
      "installUsingApt": true
    },
    "ghcr.io/devcontainers/features/python:1": {
      "version": "3.12"
    },
    "ghcr.io/devcontainers/features/docker-outside-of-docker:1": {
      "moby": false,
      "installDockerBuildx": true
    },
    "ghcr.io/eitsupi/devcontainer-features/delta:0": {},
    "ghcr.io/astral-sh/uv-devcontainer-feature/uv:latest": {}
  },
  "runArgs": [
    "--name=reference-service-techstack-comparison",
    "--cap-add=NET_ADMIN",
    "--cap-add=NET_RAW"
  ],
  "customizations": {
    "vscode": {
      "extensions": [
        "dbaeumer.vscode-eslint",
        "esbenp.prettier-vscode",
        "ms-dotnettools.vscode-dotnet-runtime",
        "ms-dotnettools.csharp",
        "ms-dotnettools.csdevkit",
        "bierner.markdown-mermaid",
        "ms-azuretools.vscode-docker"
      ],
      "settings": {
        "editor.formatOnSave": true,
        "editor.defaultFormatter": "esbenp.prettier-vscode",
        "editor.codeActionsOnSave": {
          "source.fixAll.eslint": "explicit"
        },
        "terminal.integrated.defaultProfile.linux": "zsh",
        "terminal.integrated.profiles.linux": {
          "bash": {
            "path": "bash",
            "icon": "terminal-bash"
          },
          "zsh": {
            "path": "zsh"
          }
        },
        "scm.defaultViewMode": "tree"
      }
    }
  },
  "remoteUser": "node",
  "updateRemoteUserUID": true,
  "mounts": [
    "source=reference-service-techstack-comparison-claude-code-bashhistory-${devcontainerId},target=/commandhistory,type=volume",
    "source=reference-service-techstack-comparison-claude-code-config-${devcontainerId},target=/home/node/.claude,type=volume",
    "source=/var/run/docker.sock,target=/var/run/docker.sock,type=bind"
  ],
  "containerEnv": {
    "NODE_OPTIONS": "--max-old-space-size=4096",
    "CLAUDE_CONFIG_DIR": "/home/node/.claude",
    "CLAUDE_CODE_VERSION": "latest",
    "POWERLEVEL9K_DISABLE_GITSTATUS": "true",
    "CONTEXT7_API_KEY": "${localEnv:CONTEXT7_API_KEY}",
    "EDITOR": "code --wait",
    "DEVCONTAINER": "true"
  },
  "workspaceMount": "source=${localWorkspaceFolder},target=/workspace,type=bind,consistency=delegated",
  "workspaceFolder": "/workspace",
  "postCreateCommand": "bash /workspace/.devcontainer/postcreate-wrapper.sh",
  "postStartCommand": "bash /workspace/.devcontainer/poststart-wrapper.sh",
  "waitFor": "postStartCommand"
}
```

---

## Rollback Plan

If any phase fails and cannot be fixed:

```bash
# Option 1: Revert to backup branch
git checkout .devcontainer/

# Option 2: Full revert
git checkout devcontainer-feature-migration~1 -- .devcontainer/

# Then rebuild container
```

---

## Summary Checklist

| Phase | Component | Risk | Verification |
|-------|-----------|------|--------------|
| 1 | Baseline | None | Document versions |
| 2 | Empty features | None | Container builds |
| 3 | GitHub CLI | Low | `gh --version` |
| 4 | Git Delta | Low | `delta --version` |
| 5 | .NET SDK | Medium | `dotnet build` + tests |
| 6 | Python + uv | Medium | `uvx` works |
| 7 | Docker CLI | High | `docker ps` + Testcontainers |
| 8 | Zsh | Medium | Shell + fzf work |
| 9 | Cleanup | Medium | All tools present |
| 10 | Final | Complete | Full test suite |

---

## Components That Must Remain Custom

These cannot be replaced with devcontainer features:

1. **Firewall Configuration** (`init-firewall.sh`)
   - Allowlist-based iptables/ipset rules
   - CDN-aware domain resolution
   - Docker-aware chain management

2. **Docker iptables Chains** (`fix-docker-iptables.sh`)
   - Creates Docker-specific chains
   - Allows Docker subnet traffic (172.16.0.0/12)

3. **Testcontainers Network** (`connect-to-testcontainers-network.sh`)
   - Creates and connects to custom Docker network

4. **Claude Code Setup** (`setup-claudecode.sh`)
   - MCP server registration
   - Plugin installation
   - ccstatusline configuration

5. **k6 Installation** (`install-k6.sh`)
   - Custom binary download to ~/.local/bin

6. **Docker Group GID** (Dockerfile)
   - WSL2-specific GID 1001 for socket access
