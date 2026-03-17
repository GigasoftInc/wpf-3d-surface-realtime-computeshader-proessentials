using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Gigasoft.ProEssentials;
using Gigasoft.ProEssentials.Enums;
using System.Windows.Media;

namespace RealtimeSurface3D
{
    /// <summary>
    /// ProEssentials WPF 3D Realtime Surface — ComputeShader + CircularBuffer
    ///
    /// Demonstrates the fastest possible realtime 3D surface update using
    /// Pe3doWpf with GPU compute shaders and a circular buffer append strategy.
    ///
    /// Performance highlights:
    ///   - 800 subsets × 900 points = 720,000 vertices rebuilt every 15ms
    ///   - ComputeShader: GPU builds the polygon mesh — potentially 2000+ GPU
    ///     cores do the work instead of a single CPU core
    ///   - CircularBuffer: AppendData() advances a pointer only — no data is
    ///     physically moved or shifted in memory. The GPU reads from wherever
    ///     the pointer currently is and wraps around the ring automatically.
    ///     Without CircularBuffer, every append would require shifting all
    ///     720,000 existing values in memory to make room for the new column.
    ///   - DuplicateDataX / DuplicateDataZ: only one row of X and one column
    ///     of Z values are stored — the chart duplicates them internally,
    ///     avoiding the need to pass 720,000 X and Z values per update
    ///   - SkipRanging: manual Y scale eliminates per-point range checking
    ///   - ReuseDataZ: Z never changes — tells the GPU not to re-upload it
    ///
    /// The net result: each 15ms timer tick appends one new column of 800
    /// elevation values, advances the X window, and triggers a full 720K
    /// vertex GPU rebuild — entirely without CPU polygon construction or
    /// memory shifting.
    ///
    /// Data file:
    ///   terrain2.bin — 2,250,000 Int32 elevation values (1500x1500 grid)
    ///   Columns are read sequentially and fed into the surface as a
    ///   continuous real-time stream, wrapping back to column 0 at the end.
    ///
    /// Controls:
    ///   Left drag     — rotate
    ///   Shift + drag  — pan
    ///   Mouse wheel   — zoom
    ///   Double-click  — start/stop auto-rotation
    ///   Right-click   — context menu
    /// </summary>
    public partial class MainWindow : Window
    {
        // -----------------------------------------------------------------------
        // Real-time state
        //
        // m_nRealTimeCounter: current column index in terrain2.bin being read.
        //   Wraps back to 0 after column 1499 — creates an infinite stream.
        //
        // m_nOverallCounter: monotonically increasing X axis value.
        //   Used to set ManualMinX / ManualMaxX so the X window scrolls forward.
        //
        // pGlobalElevData: the full terrain2.bin loaded once into memory at init.
        //   800 subsets × 1500 columns = columns are read as: (s * 1500) + col
        // -----------------------------------------------------------------------
        private int     m_nRealTimeCounter = 900;
        private int     m_nOverallCounter  = 900;
        private Int32[] pGlobalElevData    = new Int32[2250000];

        // 15ms timer — fastest interval, drives ~66 full 720K-vertex rebuilds/second
        private DispatcherTimer Timer1 = new DispatcherTimer(DispatcherPriority.Input);

        public MainWindow()
        {
            InitializeComponent();

            Timer1.Tick    += new EventHandler(Timer1_Tick);
            Timer1.Interval = new TimeSpan(0, 0, 0, 0, 15);
        }

        // -----------------------------------------------------------------------
        // Pe3do1_Loaded — chart initialization
        // -----------------------------------------------------------------------
        void Pe3do1_Loaded(object sender, RoutedEventArgs e)
        {
            // =======================================================================
            // Step 1 — Load terrain2.bin into memory
            //
            // The entire 1500×1500 elevation grid is loaded once at startup.
            // During real-time updates, one column of 800 values is read per tick.
            // =======================================================================
            string filepath = System.AppDomain.CurrentDomain.BaseDirectory + "\\terrain2.bin";
            FileStream fs = null;
            try
            {
                fs = new FileStream(filepath, FileMode.Open);
            }
            catch
            {
                MessageBox.Show(
                    "terrain2.bin not found.\n\nMake sure terrain2.bin is in the same folder as the executable.",
                    "File Not Found", MessageBoxButton.OK);
                Application.Current.Shutdown();
                return;
            }

            BinaryReader r = new BinaryReader(fs);
            for (int o = 0; o < 2250000; o++)
                pGlobalElevData[o] = r.ReadInt32();
            fs.Close();

            // =======================================================================
            // Step 2 — Set data dimensions and pass initial surface data
            //
            // 800 subsets × 900 points = 720,000 data points per frame.
            //
            // DuplicateDataX = PointIncrement: only one row of X values needed.
            //   The chart generates X[s,p] = X[0,p] for all subsets automatically.
            //   Saves passing 800 × 900 = 720,000 X values.
            //
            // DuplicateDataZ = SubsetIncrement: only one column of Z values needed.
            //   The chart generates Z[s,p] = Z[0,s] for all points automatically.
            //   Saves passing another 720,000 Z values.
            //
            // Setting Y[799,899] = 0 first pre-allocates the full internal Y array
            // before the spoon-feed loop below — more efficient than growing it
            // incrementally.
            // =======================================================================
            Pe3do1.PeData.Subsets = 800;
            Pe3do1.PeData.Points  = 900;

            Pe3do1.PeData.DuplicateDataX = DuplicateData.PointIncrement;
            Pe3do1.PeData.DuplicateDataZ = DuplicateData.SubsetIncrement;

            Pe3do1.PeData.Y[799, 899] = 0; // pre-allocate full Y array

            int l, s, p;
            for (s = 0; s < 800; s++)
                for (p = 0; p < 900; p++)
                {
                    l = (s * 1500) + p;
                    Pe3do1.PeData.Y[s, p] = pGlobalElevData[l] * 0.1F;
                }

            // X axis: one value per point column (duplicated across all subsets)
            for (p = 0; p < 900; p++)
                Pe3do1.PeData.X[0, p] = (float)p + 1;

            // Z axis: one value per subset row (duplicated across all points)
            for (s = 0; s < 800; s++)
                Pe3do1.PeData.Z[0, s] = (float)s + 1;

            // =======================================================================
            // Step 3 — Surface rendering
            // =======================================================================

            Pe3do1.PeColor.SubsetColors[(int)SurfaceColors.WireFrame]    = Color.FromArgb(255, 215, 215, 255);
            Pe3do1.PeColor.SubsetColors[(int)SurfaceColors.SolidSurface] = Color.FromArgb(255, 215, 215, 255);

            // ThreeDGraphPlottingMethod.Four = Surface with contour color fill
            Pe3do1.PePlot.Method               = ThreeDGraphPlottingMethod.Four;
            Pe3do1.PePlot.Allow.SurfaceContour = true;
            Pe3do1.PePlot.Option.ShowWireFrame = false;

            // Direct3D is required for Pe3doWpf and for ComputeShader support
            Pe3do1.PeConfigure.RenderEngine = RenderEngine.Direct3D;

            // =======================================================================
            // Step 4 — Camera / view defaults
            // These values were tuned to show the rolling terrain panorama optimally.
            // DegreePrompting = true shows live view parameters in the top-left
            // corner — useful for finding your own preferred camera position.
            // =======================================================================
            Pe3do1.PeUserInterface.Scrollbar.ViewingHeight    = 18;
            Pe3do1.PeUserInterface.Scrollbar.DegreeOfRotation = 92;

            Pe3do1.PeFunction.SetLight(0, 7.36F, -7.59F, 3.52F);

            Pe3do1.PePlot.Option.DxZoom         = -7.01F;
            Pe3do1.PePlot.Option.DxViewportX    = .66F;
            Pe3do1.PePlot.Option.DxViewportY    = 2.6F;
            Pe3do1.PePlot.Option.DxZoomMax      = -6F;
            Pe3do1.PePlot.Option.DxZoomMin      = -10F;
            Pe3do1.PePlot.Option.DxViewportPanFactor             = 2.0F;
            Pe3do1.PeUserInterface.Scrollbar.MouseWheelZoomFactor = 3.75F;

            // =======================================================================
            // Step 5 — Manual scale
            //
            // Manually setting all three axes prevents ProEssentials from
            // range-checking 720,000 points every frame — critical for performance.
            // SkipRanging in the timer tick reinforces this.
            // =======================================================================
            Pe3do1.PeGrid.Configure.ManualScaleControlY = ManualScaleControl.MinMax;
            Pe3do1.PeGrid.Configure.ManualMinY          = 600;
            Pe3do1.PeGrid.Configure.ManualMaxY          = 1800;

            Pe3do1.PeGrid.Configure.ManualScaleControlX = ManualScaleControl.MinMax;
            Pe3do1.PeGrid.Configure.ManualMinX          = 1;
            Pe3do1.PeGrid.Configure.ManualMaxX          = 900;

            Pe3do1.PeGrid.Configure.ManualScaleControlZ = ManualScaleControl.MinMax;
            Pe3do1.PeGrid.Configure.ManualMinZ          = 1;
            Pe3do1.PeGrid.Configure.ManualMaxZ          = 800;

            // =======================================================================
            // Step 6 — Contour color scale
            //
            // Custom color ramp: deep blue (low elevation) → green → grey/white (peaks)
            // SubsetShades provides the monochrome fallback for the greyscale view mode.
            // =======================================================================
            Pe3do1.PeLegend.ContourLegendPrecision = ContourLegendPrecision.ZeroDecimals;
            Pe3do1.PeLegend.Location               = LegendLocation.Right;
            Pe3do1.PeLegend.ContourStyle           = true;

            Pe3do1.PeColor.SubsetColors.Clear();

            // Deep blue → cyan (low elevation)
            for (s = 0; s <= 31; s++)
                Pe3do1.PeColor.SubsetColors[s] = Color.FromArgb(255, 0, (byte)(31 + (s * 7)), (byte)(95 + (s * 5)));

            // Green band (mid elevation)
            for (s = 0; s <= 31; s++)
                Pe3do1.PeColor.SubsetColors[32 + s] = Color.FromArgb(255, 0, (byte)(95 + (s * 5)), 0);

            // Grey → white (high elevation / peaks)
            for (s = 0; s <= 35; s++)
                Pe3do1.PeColor.SubsetColors[64 + s] = Color.FromArgb(255,
                    (byte)(128 + (s * 3)), (byte)(128 + (s * 3)), (byte)(128 + (s * 3)));

            // Monochrome shades for greyscale viewing mode
            for (s = 0; s <= 99; s++)
                Pe3do1.PeColor.SubsetShades[s] = Color.FromArgb(255,
                    (byte)(50 + (s * 2)), (byte)(50 + (s * 2)), (byte)(50 + (s * 2)));

            Pe3do1.PeLegend.ContourStyle = true;
            Pe3do1.PeLegend.Show         = true;
            Pe3do1.PeLegend.Location     = LegendLocation.Right;
            Pe3do1.PeUserInterface.Menu.LegendLocation = MenuControl.Show;

            // =======================================================================
            // Step 7 — Visual styling
            // =======================================================================
            Pe3do1.PeColor.BitmapGradientMode = false;
            Pe3do1.PeColor.QuickStyle         = QuickStyle.DarkNoBorder;

            Pe3do1.PeString.MainTitle = "720K vertices rendered each update";
            Pe3do1.PeString.SubTitle  = "";

            Pe3do1.PePlot.Option.DegreePrompting = true; // shows live view parameters top-left

            // GridAspectX/Z expand the horizontal extents for a panoramic view
            Pe3do1.PeGrid.Option.GridAspectX = 10.0F;
            Pe3do1.PeGrid.Option.GridAspectZ = 5.0F;

            Pe3do1.PeFont.Fixed               = true;
            Pe3do1.PeFont.FontSize            = Gigasoft.ProEssentials.Enums.FontSize.Medium;
            Pe3do1.PeConfigure.PrepareImages  = true;
            Pe3do1.PeConfigure.CacheBmp       = true;
            Pe3do1.PeUserInterface.Allow.FocalRect = false;

            Pe3do1.PeUserInterface.Menu.Contour    = MenuControl.Hide;
            Pe3do1.PeUserInterface.Menu.Rotation   = MenuControl.Hide;
            Pe3do1.PeUserInterface.Menu.BorderType = MenuControl.Hide;

            Pe3do1.PeConfigure.TextShadows    = TextShadows.BoldText;
            Pe3do1.PeFont.Label.Bold          = true;
            Pe3do1.PeConfigure.ImageAdjustRight = 50;
            Pe3do1.PeConfigure.ImageAdjustLeft  = 50;

            Pe3do1.PeUserInterface.Scrollbar.MouseDraggingX = true;
            Pe3do1.PeUserInterface.Scrollbar.MouseDraggingY = true;

            // Export defaults
            Pe3do1.PeSpecial.DpiX = 600;
            Pe3do1.PeSpecial.DpiY = 600;
            Pe3do1.PeUserInterface.Dialog.ExportSizeDef  = ExportSizeDef.NoSizeOrPixel;
            Pe3do1.PeUserInterface.Dialog.ExportTypeDef  = ExportTypeDef.Png;
            Pe3do1.PeUserInterface.Dialog.ExportDestDef  = ExportDestDef.Clipboard;
            Pe3do1.PeUserInterface.Dialog.ExportUnitXDef = "1280";
            Pe3do1.PeUserInterface.Dialog.ExportUnitYDef = "768";
            Pe3do1.PeUserInterface.Dialog.ExportImageDpi = 300;
            Pe3do1.PeUserInterface.Dialog.AllowEmfExport = false;
            Pe3do1.PeUserInterface.Dialog.AllowWmfExport = false;

            // =======================================================================
            // Step 8 — ComputeShader + CircularBuffer
            //
            // This is the performance core of the example. These five lines are
            // what make 720K vertex rebuilds at 15ms possible.
            //
            // ComputeShader = true: delegates polygon mesh construction to the GPU.
            //   Instead of the CPU computing vertex positions for 720,000 polygons,
            //   potentially 2000+ GPU cores do it in parallel. The difference is
            //   dramatic — comment these lines out to experience CPU-only construction.
            //
            // StagingBufferX/Y/Z = true: required when ComputeShader is enabled.
            //   Staging buffers are intermediate GPU-accessible memory regions that
            //   allow efficient data transfer from CPU to GPU without stalling the
            //   render pipeline.
            //
            // CircularBuffers = true: enables zero-copy circular buffer append.
            //   AppendData() advances an internal pointer and writes new data at
            //   the current position — no existing data is physically moved or
            //   shifted in memory. The GPU reads from wherever the pointer is
            //   and wraps around the ring automatically.
            //   Without CircularBuffers, every AppendData() call would need to
            //   shift all existing Y values in memory to make room for the new
            //   column — O(n) work per update. With CircularBuffers it is O(1).
            // =======================================================================
            Pe3do1.PeData.ComputeShader   = true;
            Pe3do1.PeData.StagingBufferX  = true;
            Pe3do1.PeData.StagingBufferY  = true;
            Pe3do1.PeData.StagingBufferZ  = true;
            Pe3do1.PeData.CircularBuffers = true;
            // Comment the five lines above to compare CPU construction vs GPU ComputeShader.
            // The performance difference demonstrates the power of 2000+ GPU cores
            // versus a single CPU core building polygon data sequentially.

            // Force3dxVerticeRebuild signals the chart to rebuild low-level vertex content
            Pe3do1.PeFunction.Force3dxVerticeRebuild = true;

            Pe3do1.PeFunction.ReinitializeResetImage();
            Pe3do1.Invalidate();
            Pe3do1.UpdateLayout();

            // Start the 15ms timer — fires Timer1_Tick ~66 times per second
            Timer1.Start();
        }

        // -----------------------------------------------------------------------
        // Timer1_Tick — real-time surface update (15ms interval)
        //
        // Each tick appends one new column of terrain data to the scrolling surface:
        //
        // 1. Build pNewYData[800]: read one column from pGlobalElevData for all
        //    800 subsets. Each subset reads from offset (s * 1500) + m_nRealTimeCounter.
        //
        // 2. Build pNewXData[800]: all 800 values = m_nOverallCounter (same X for
        //    the entire new column — the X axis value for this point in time).
        //
        // 3. Advance m_nRealTimeCounter — wraps to 0 at end of terrain file,
        //    creating a seamless infinite loop through the terrain data.
        //
        // 4. Advance ManualMinX / ManualMaxX to scroll the X window forward,
        //    keeping the visible range always 900 points wide.
        //
        // 5. AppendData(pNewYData, 1) — appends one new point column to Y.
        //    With CircularBuffers enabled, this just advances a pointer and
        //    writes 800 values in place — zero memory shifting, O(1) cost.
        //
        // 6. AppendData(pNewXData, 1) — appends the new X column similarly.
        //
        // 7. ReuseDataZ = true — Z coordinates never change (always subset 0-799).
        //    This tells the GPU not to re-upload Z data, saving bandwidth.
        //
        // 8. PEreconstruct3dpolygons() — triggers the ComputeShader to rebuild
        //    all 720,000 polygon vertices on the GPU.
        // -----------------------------------------------------------------------
        void Timer1_Tick(object sender, EventArgs e)
        {
            // Appends multiple columns per tick to increase scroll speed.
            // 20 columns per tick produces a dramatic effect where the entire
            // scene visibly rebuilds each frame — try values from 1 to 50 to
            // find the speed that works best for your use case.
            //
            // A potentially more efficient approach would be to pre-build all
            // 20 columns into a single larger array and call AppendData once —
            // this works well and is simple to understand.
            //
            // Note: this is an intentionally simple project — replace the
            // ProEssentials NuGet package with a competitor to compare
            // realtime 3D surface performance head-to-head.
            float[] pNewXData = new float[800];
            float[] pNewYData = new float[800];

            int l, s;

            for (int col = 0; col < 20; col++)
            {
                for (s = 0; s < 800; s++)
                {
                    pNewXData[s] = m_nOverallCounter;
                    l = (s * 1500) + m_nRealTimeCounter;
                    pNewYData[s] = pGlobalElevData[l] * 0.1F;
                }

                m_nRealTimeCounter++;
                if (m_nRealTimeCounter > 1499) { m_nRealTimeCounter = 0; } // wrap — infinite terrain stream

                m_nOverallCounter++;

                // Scroll the X axis window forward by one point
                Pe3do1.PeGrid.Configure.ManualMinX = m_nOverallCounter - 900;
                Pe3do1.PeGrid.Configure.ManualMaxX = m_nOverallCounter;

                // SkipRanging: manual scale — skip per-point range checking across 720,000 points
                Pe3do1.PeData.SkipRanging = true;

                // AppendData(data, 1): O(1) pointer advance + 800-value write via CircularBuffer
                Pe3do1.PeData.Y.AppendData(pNewYData, 1);
                Pe3do1.PeData.X.AppendData(pNewXData, 1);

                // ReuseDataZ: Z values never change — skip GPU Z data re-upload
                Pe3do1.PeData.ReuseDataZ = true;
            }

            // Trigger GPU ComputeShader to rebuild all 720,000 polygon vertices once per tick
            Pe3do1.PeFunction.PEreconstruct3dpolygons();
            Pe3do1.Invalidate();
        }

        // -----------------------------------------------------------------------
        // Window_Closing — stop timer cleanly on exit
        // -----------------------------------------------------------------------
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Timer1.Stop();
        }
    }
}
