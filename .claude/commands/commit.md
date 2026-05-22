---
allowed-tools: Bash(git add:*), Bash(git status:*), Bash(git commit:*), Bash(git push:*)
description: Create a git commit and push to origin
---

## Context

- Current git status: !`git status`
- Current git diff (staged and unstaged changes): !`git diff HEAD`
- Current branch: !`git branch --show-current`
- Recent commits: !`git log --oneline -10`

## Your task

Based on the above changes, create a single git commit, then push it to origin.

You have the capability to call multiple tools in a single response. Stage, create the commit, and push in a single message. Do not use any other tools or do anything else. Do not send any other text or messages besides these tool calls. Do not force-push.
