using System.Windows;
using System.Windows.Controls;
using RobloxLiveTranslator.Services;

namespace RobloxLiveTranslator;

public partial class OcrLanguagePickerWindow : Window
{
    private readonly (string Code, CheckBox Check)[] _items;
    public string SelectedLanguages { get; private set; }

    public OcrLanguagePickerWindow(string currentLanguages)
    {
        InitializeComponent();
        _items =
        [
            ("eng", EngCheck), ("kor", KorCheck), ("jpn", JpnCheck),
            ("chi_sim", ChiSimCheck), ("chi_tra", ChiTraCheck), ("spa", SpaCheck),
            ("fra", FraCheck), ("deu", DeuCheck), ("rus", RusCheck), ("por", PorCheck),
            ("ita", ItaCheck), ("vie", VieCheck), ("tha", ThaCheck), ("ind", IndCheck),
            ("hin", HinCheck), ("ara", AraCheck)
        ];

        var selected = ModelManager.ParseLanguages(string.IsNullOrWhiteSpace(currentLanguages) ? "eng+kor" : currentLanguages)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _items)
        {
            item.Check.IsChecked = selected.Contains(item.Code);
            item.Check.Checked += SelectionChanged;
            item.Check.Unchecked += SelectionChanged;
        }
        SelectedLanguages = BuildValue();
        RefreshSummary();
    }

    private void SelectionChanged(object sender, RoutedEventArgs e) => RefreshSummary();

    private string BuildValue() => string.Join("+", _items.Where(x => x.Check.IsChecked == true).Select(x => x.Code));

    private void RefreshSummary()
    {
        var count = _items.Count(x => x.Check.IsChecked == true);
        SummaryText.Text = count == 0 ? "최소 1개 언어가 필요합니다." : $"선택 {count}개 · {BuildValue()}";
    }

    private void EnglishKoreanPreset_Click(object sender, RoutedEventArgs e) => SetOnly("eng", "kor");
    private void EastAsiaPreset_Click(object sender, RoutedEventArgs e) => SetOnly("eng", "kor", "jpn", "chi_sim", "chi_tra");
    private void Clear_Click(object sender, RoutedEventArgs e) => SetOnly();

    private void SetOnly(params string[] codes)
    {
        var set = codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _items) item.Check.IsChecked = set.Contains(item.Code);
        RefreshSummary();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var value = BuildValue();
        if (string.IsNullOrWhiteSpace(value))
        {
            MessageBox.Show(this, "OCR 언어를 하나 이상 선택하세요.", "RoiLingo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedLanguages = value;
        DialogResult = true;
    }
}
