# Amba — Unity SDK

Amba is the agent-native backend-as-a-service for mobile and web apps. This package is the Unity SDK (`com.layers.amba`), distributed via Unity Package Manager.

Supported build targets: iOS, Android, macOS, Windows, Linux.

## Install

In Unity, open **Window → Package Manager → + → Add package from git URL** and paste:

```
https://github.com/layers/amba-sdk-unity.git#4.0.0
```

Or in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.layers.amba": "https://github.com/layers/amba-sdk-unity.git#4.0.0"
  }
}
```

## Configure + first call

```csharp
using Layers.Amba;
using System.Collections.Generic;
using UnityEngine;

public class Bootstrap : MonoBehaviour
{
    async void Start()
    {
        await Amba.ConfigureAsync(apiKey: "amba_pk_…");
        await Amba.Auth.SignInAnonymouslyAsync();
        await Amba.Events.TrackAsync("game_started", new Dictionary<string, object>
        {
            { "level", 1 },
        });
    }
}
```

## Docs

Full reference: <https://docs.amba.dev/sdk/unity>.

## License

MIT
