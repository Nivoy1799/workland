# /infra

**Purpose:** k3s cluster on rented servers, Agones game server fleets, Ansible provisioning,
Prometheus + Grafana monitoring. Hoster is decision O5 (OPEN).

**Typically built by:** builder on Sonnet (manifests, playbooks), reviewer (Opus) checks it.

**How to test:** not set up yet; lint/dry-run checks get added to `scripts/verify.sh` with their spec.
