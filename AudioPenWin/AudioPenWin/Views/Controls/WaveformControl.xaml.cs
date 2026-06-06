using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace AudioPenWin.Views.Controls;

public sealed partial class WaveformControl : UserControl
{
    private static readonly Color BarColor = Color.FromArgb(255, 100, 149, 237);

    private readonly float[] _bars = new float[60];
    private int _head;

    public WaveformControl()
    {
        InitializeComponent();
    }

    public void AddSample(float amplitude)
    {
        _bars[_head] = amplitude;
        _head = (_head + 1) % _bars.Length;
        WaveCanvas.Invalidate();
    }

    public void Reset()
    {
        Array.Clear(_bars);
        _head = 0;
        WaveCanvas.Invalidate();
    }

    private void WaveCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        float w = (float)sender.ActualWidth;
        float h = (float)sender.ActualHeight;
        if (w <= 0 || h <= 0) return;

        float barW = w / _bars.Length;

        for (int i = 0; i < _bars.Length; i++)
        {
            int idx = (_head + i) % _bars.Length;
            float barH = Math.Max(2f, _bars[idx] * h * 0.85f);
            float x = i * barW + 1f;
            float y = (h - barH) / 2f;
            ds.FillRectangle(x, y, barW - 2f, barH, BarColor);
        }
    }
}
