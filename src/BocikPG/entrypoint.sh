#!/bin/bash
set -e

RESOURCES_DIR="/app/Resources"
REMOTE_URL="${GitSync__REMOTE:-}"
BRANCH="${GitSync__Branch:-main}"
GIT_USER_NAME="${GitSync__User_Name:-BocikPG Bot}"
GIT_USER_EMAIL="${GitSync__User_Email:-bot@localhost}"

log() {
    echo "[$(date +'%Y-%m-%d %H:%M:%S')] $1"
}

mkdir -p "$RESOURCES_DIR"

if [ -d "$RESOURCES_DIR/.git" ]; then
    log "Git repository already exists in $RESOURCES_DIR"
    if [ -n "$REMOTE_URL" ]; then
        if git -C "$RESOURCES_DIR" remote | grep -q "^origin$"; then
            CURRENT_URL=$(git -C "$RESOURCES_DIR" remote get-url origin)
            if [ "$CURRENT_URL" != "$REMOTE_URL" ]; then
                log "Updating origin URL from $CURRENT_URL to $REMOTE_URL"
                git -C "$RESOURCES_DIR" remote set-url origin "$REMOTE_URL"
            fi
        else
            log "Adding remote origin: $REMOTE_URL"
            git -C "$RESOURCES_DIR" remote add origin "$REMOTE_URL"
        fi
        log "Pulling latest changes from $BRANCH branch..."
        git -C "$RESOURCES_DIR" pull origin "$BRANCH" --no-rebase || log "Pull failed (maybe empty repo or conflicts)"
    fi
else
    log "Initialising new git repository in $RESOURCES_DIR"
    git -C "$RESOURCES_DIR" init
    
    # Set git user identity for this repository
    git -C "$RESOURCES_DIR" config user.name "$GIT_USER_NAME"
    git -C "$RESOURCES_DIR" config user.email "$GIT_USER_EMAIL"
    
    if [ -n "$REMOTE_URL" ]; then
        log "Adding remote origin: $REMOTE_URL"
        git -C "$RESOURCES_DIR" remote add origin "$REMOTE_URL"
        log "Fetching and pulling from $BRANCH branch..."
        git -C "$RESOURCES_DIR" fetch origin || log "Fetch failed (remote may be empty or unreachable)"
        git -C "$RESOURCES_DIR" pull origin "$BRANCH" --no-rebase || log "Pull failed (branch may not exist yet)"
    else
        log "No remote URL provided (set GitSync__Remote env var). Skipping remote setup."
    fi
    
    if [ -z "$(git -C "$RESOURCES_DIR" ls-files)" ]; then
        log "Creating initial empty commit"
        git -C "$RESOURCES_DIR" commit --allow-empty -m "Initial empty commit"
    fi
fi

# Ensure bot's working directory is /app
cd /app
log "Starting Discord bot..."
exec dotnet /app/BocikPG.dll