# Agent Notes

This repository uses DevPod with a devcontainer.

Canonical workspace name:

```bash
intervals
```

Canonical container name:

```bash
intervals-devcontainer
```

Preferred way to enter the workspace:

```bash
devpod ssh intervals
```

Fallback when a tool needs direct Docker access:

```bash
docker exec -it intervals-devcontainer bash
```

The devcontainer sets the Docker container name through `.devcontainer/devcontainer.json`
using `runArgs`, so agents should not depend on auto-generated Docker container names.
