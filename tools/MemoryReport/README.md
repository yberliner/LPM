# Memory Report Generator

Generates a per-circuit RAM inventory for the LPM Blazor Server app as a single A3-landscape PDF.
Each row = one field stored in server memory per browser tab, with type, size estimate, and what it
holds. Sorted big-to-small by typical bytes.

## Usage

```
cd Tools/MemoryReport
dotnet run -c Release
```

Default output: `LPM_Server/PerCircuitMemoryReport.pdf` (overwrites on each run).

Pass an output path to override:

```
dotnet run -c Release -- "C:\path\to\custom-output.pdf"
```

## When to regenerate

After significant changes to any of these files (which is when the field inventory shifts):

- `LPM_Server/Pages/PcFolder.razor`
- `LPM_Server/Pages/Home.razor`
- `LPM_Server/Shared/MainHeader.razor`
- New page added under `LPM_Server/Pages/`
- New service registered as `AddScoped<>` in `LPM_Server/Program.cs`

To update the inventory, edit the `rows` list at the top of `Program.cs`. Each row is:

```csharp
(Component, Field, Type, BytesTypical, BytesWorst, Notes)
```

The Notes column should explain *what the field holds* and *why it might grow* — that's how the
reader figures out where the memory is going. Keep it specific (e.g. "list of pending uploads, raw
bytes, 5×1MB worst case") not vague ("upload state").

## Estimation rules used

- `bool` = 1 byte, `int`/`enum` = 4, `long`/`double`/`DateTime` = 8
- `string` typical (paths, names) ≈ 40 bytes; HTML/Quill bodies 10 KB–1 MB
- Empty `Dictionary`/`HashSet`/`List` overhead ≈ 40 bytes; per entry ≈ 24 bytes + key + value
- `byte[]` upload buffers: estimate from typical and worst file sizes
- `CancellationTokenSource`, `PeriodicTimer` ≈ 100 bytes when active
- Reference-type fields: 8-byte pointer (the referenced object's size is counted under *its*
  owning component, not double-counted here)

## What's intentionally NOT included

- **Singleton services** (FolderService, DashboardService, PcService, etc.) — shared across all
  circuits, not per-circuit. Listed separately if needed.
- **PDFs** — rendered client-side by PDF.js in the browser. Server only streams encrypted bytes
  through HTTP; the bytes are GC'd as soon as the response flushes.
- **DB query result holders** that are method-local — they get GC'd after the method returns.
- **`IMemoryCache` contents** — server-shared, not per-circuit.
