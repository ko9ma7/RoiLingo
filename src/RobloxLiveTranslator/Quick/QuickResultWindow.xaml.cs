using System.Windows;
using RobloxLiveTranslator.Native;

namespace RobloxLiveTranslator.Quick;

public partial class QuickResultWindow : Window
{
    private readonly Rect? _anchor;

    public QuickResultWindow(string title, string source, string translation, string provider, string meta, Rect? anchor = null)
    {
        InitializeComponent();
        _anchor = anchor;
        HeaderText.Text = title;
        SourceBox.Text = source;
        TranslationBox.Text = translation;
        MetaText.Text = string.IsNullOrWhiteSpace(meta) ? provider : $"{provider} · {meta}";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CaptureExclusion.Apply(this);
        if (_anchor is not Rect anchor) return;
        var work = SystemParameters.WorkArea;
        var left = anchor.Right + 12;
        if (left + Width > work.Right) left = anchor.Left - Width - 12;
        Left = Math.Max(work.Left, Math.Min(left, work.Right - Width));
        Top = Math.Max(work.Top, Math.Min(anchor.Top, work.Bottom - Height));
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(TranslationBox.Text ?? string.Empty); }
        catch { }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
