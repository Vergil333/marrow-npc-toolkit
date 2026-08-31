# Compatibility

| Component | Preview status |
| --- | --- |
| Unity | 2021.3.16f1 |
| Marrow SDK | Official 1.2.0 |
| BONELAB | Patch 6 exact compatibility profile |
| Quest / Android packing | Supported; an Eve development build was runtime-tested, while the exact public candidate cold test remains pending |
| Windows packing | Exact candidate packed and structurally inspected; clean Windows BONELAB runtime test pending |
| Extended SDK | Not supported by 0.5.0-preview.1 |
| General Humanoids | Intended, but only Eve is currently runtime-proven |

The provider probes exact assemblies, types, fields, and reference inputs. It
must report unavailable instead of guessing when an SDK or game update changes
the serialized contract.

Passing Unity checks proves only that the saved assets satisfy the inspected
contract. A release candidate must still be spawned and exercised in BONELAB on
each advertised platform.
