# ProEssentials WPF 3D Realtime Surface — ComputeShader + CircularBuffer

A ProEssentials v10 WPF .NET 8 demonstration of the fastest possible
realtime 3D surface update — 720,000 vertices rebuilt every 15ms using
GPU compute shaders and a zero-copy circular buffer append strategy.

![ProEssentials 3D Realtime Surface](https://gigasoft.com/wpf-chart/screenshots/screen413.png)

➡️ [gigasoft.com/examples/413](https://gigasoft.com/examples/413)

---

## Performance

| Metric | Value |
|--------|-------|
| Surface dimensions | 800 subsets × 900 points |
| Vertices rebuilt per update | **720,000** |
| Timer interval | **15ms** |
| Polygon construction | **GPU ComputeShader** |
| Data append cost | **O(1) — CircularBuffer** |

---

## How It Works

### ComputeShader — GPU polygon construction

Without ComputeShader, the CPU must compute vertex positions for all
720,000 polygons every 15ms — sequential work on a single core.

With ComputeShader enabled, the GPU does this work instead — potentially
2,000+ GPU cores operating in parallel. The difference is dramatic.
Comment out the five lines below to compare:

```csharp
Pe3do1.PeData.ComputeShader   = true;
Pe3do1.PeData.StagingBufferX  = true;
Pe3do1.PeData.StagingBufferY  = true;
Pe3do1.PeData.StagingBufferZ  = true;
Pe3do1.PeData.CircularBuffers = true;
```

### CircularBuffer — O(1) zero-copy append

Without CircularBuffers, every `AppendData()` call shifts all existing
data in memory to make room for the new column — O(n) work that grows
more expensive as the buffer fills.

With CircularBuffers enabled, `AppendData()` simply advances an internal
pointer and writes the new data at the current ring position. No existing
data is moved. The GPU reads from wherever the pointer is and wraps around
the ring automatically. The cost is always O(1) regardless of buffer size.

```csharp
// Each tick — appends one new column of 800 elevation values.
// With CircularBuffers: pointer advance + 800-value write. Zero shifting.
Pe3do1.PeData.Y.AppendData(pNewYData, 1);
Pe3do1.PeData.X.AppendData(pNewXData, 1);
```

### DuplicateData — avoid redundant axis data

```csharp
Pe3do1.PeData.DuplicateDataX = DuplicateData.PointIncrement;
Pe3do1.PeData.DuplicateDataZ = DuplicateData.SubsetIncrement;
```

Only one row of X values and one column of Z values are stored. The
chart duplicates them internally — avoids passing 720,000 X and Z
values on every update.

### ReuseDataZ — skip GPU Z re-upload

```csharp
Pe3do1.PeData.ReuseDataZ = true;
```

Z coordinates never change (always subset rows 0–799). This tells the
GPU not to re-upload Z data each tick, saving memory bandwidth.

### SkipRanging — skip per-point range checking

```csharp
Pe3do1.PeData.SkipRanging = true;
```

Y scale is set manually. Range-checking 720,000 points every 15ms would
consume significant CPU. SkipRanging bypasses this entirely.

---

## The Timer Tick

Each 15ms tick:

```csharp
// Build one new column: 800 Y values from terrain file + 800 X values
for (s = 0; s < 800; s++)
{
    pNewXData[s] = m_nOverallCounter;
    pNewYData[s] = pGlobalElevData[(s * 1500) + m_nRealTimeCounter] * 0.1F;
}

// Advance column pointer — wraps at 1499 for infinite terrain stream
m_nRealTimeCounter++;
if (m_nRealTimeCounter > 1499) m_nRealTimeCounter = 0;

// Scroll X window forward
Pe3do1.PeGrid.Configure.ManualMinX = m_nOverallCounter - 900;
Pe3do1.PeGrid.Configure.ManualMaxX = m_nOverallCounter;

// Zero-copy append via CircularBuffer
Pe3do1.PeData.Y.AppendData(pNewYData, 1);
Pe3do1.PeData.X.AppendData(pNewXData, 1);
Pe3do1.PeData.ReuseDataZ = true;

// GPU ComputeShader rebuilds 720,000 polygon vertices
Pe3do1.PeFunction.PEreconstruct3dpolygons();
Pe3do1.Invalidate();
```

---

## Data File

`terrain2.bin` — 2,250,000 Int32 elevation values stored as a 1500×1500
grid. One column of 800 values is read per tick. The pointer wraps at
column 1499, creating a seamless infinite terrain stream.

The file is copied to the output directory automatically on build.

---

## Controls

| Input | Action |
|-------|--------|
| Left drag | Rotate |
| Shift + drag | Pan |
| Mouse wheel | Zoom |
| Double-click | Start/stop auto-rotation |
| Right-click | Context menu |

---

## Prerequisites

- Visual Studio 2022
- .NET 8 SDK
- Dedicated GPU recommended for full ComputeShader performance
- Internet connection for NuGet restore

---

## How to Run

```
1. Clone this repository
2. Open RealtimeSurface3D.sln in Visual Studio 2022
3. Build → Rebuild Solution (NuGet restore is automatic)
4. Press F5
```

---

## NuGet Package

References
[`ProEssentials.Chart.Net80.x64.Wpf`](https://www.nuget.org/packages/ProEssentials.Chart.Net80.x64.Wpf).
Package restore is automatic on build.

---

## Related Examples

- [WPF Quickstart](https://github.com/GigasoftInc/wpf-chart-quickstart-proessentials)
- [GigaPrime2D WPF — 100 Million Points](https://github.com/GigasoftInc/wpf-chart-fast-100m-points-proessentials)
- [3D Wellbore Flythrough](https://github.com/GigasoftInc/wpf-3d-surface-wellbore-flythrough-proessentials)
- [3D Surface Height Map](https://github.com/GigasoftInc/wpf-chart-3d-surface-proessentials)
- [All Examples — GigasoftInc on GitHub](https://github.com/GigasoftInc)
- [Full Evaluation Download](https://gigasoft.com/net-chart-component-wpf-winforms-download)
- [gigasoft.com](https://gigasoft.com)

---

## License

Example code is MIT licensed. ProEssentials requires a commercial
license for continued use.
