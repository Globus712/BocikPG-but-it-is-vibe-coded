#!/bin/bash
set -e

RESOURCES_DIR="/app/Resources"
REMOTE_URL="${GIT_SYNC_REMOTE:-}"
BRANCH="${GIT_SYNC_BRANCH:-main}"
GIT_USER_NAME="${GIT_USER_NAME:-BocikPG Bot}"
GIT_USER_EMAIL="${GIT_USER_EMAIL:-bot@localhost}"

log() {
    echo "[$(date +'%Y-%m-%d %H:%M:%S')] $1"
}

mkdir -p "$RESOURCES_DIR"

if [ -d "$RESOURCES_DIR/.git" ]; then
    log "Git repository already exists in $RESOURCES_DIR"
    
    if [ -n "$REMOTE_URL" ]; then
        # Ensure origin remote is set correctly
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
        
        log "Fetching and switching to branch $BRANCH..."
        git -C "$RESOURCES_DIR" fetch origin
        # Checkout existing branch or create tracking branch
        if git -C "$RESOURCES_DIR" show-ref --verify --quiet "refs/heads/$BRANCH"; then
            git -C "$RESOURCES_DIR" checkout "$BRANCH"
        else
            git -C "$RESOURCES_DIR" checkout -b "$BRANCH" origin/"$BRANCH"
        fi
        
        log "Pulling latest changes from $BRANCH..."
        git -C "$RESOURCES_DIR" pull origin "$BRANCH" --no-rebase || log "Pull failed (maybe conflicts)"
    fi
else
    log "Initialising new git repository in $RESOURCES_DIR"
    git -C "$RESOURCES_DIR" init
    git -C "$RESOURCES_DIR" config user.name "$GIT_USER_NAME"
    git -C "$RESOURCES_DIR" config user.email "$GIT_USER_EMAIL"
    
    # Set default branch name
    git -C "$RESOURCES_DIR" checkout -b "$BRANCH" || true
    
    if [ -n "$REMOTE_URL" ]; then
        log "Adding remote origin: $REMOTE_URL"
        git -C "$RESOURCES_DIR" remote add origin "$REMOTE_URL"
        log "Fetching and pulling from $BRANCH..."
        git -C "$RESOURCES_DIR" fetch origin || log "Fetch failed"
        git -C "$RESOURCES_DIR" pull origin "$BRANCH" --no-rebase || log "Pull failed (branch may not exist yet)"
    else
        log "No remote URL provided (set GIT_SYNC_REMOTE env var). Skipping remote setup."
    fi
    
    # Create initial empty commit only if no files and no remote
    if [ -z "$(git -C "$RESOURCES_DIR" ls-files)" ]; then
        log "Creating initial empty commit"
        git -C "$RESOURCES_DIR" commit --allow-empty -m "Initial empty commit"
    fi
fi

# Ensure bot's working directory is /app
cd /app
log "Starting Discord bot..."
exec dotnet /app/BocikPG.dll