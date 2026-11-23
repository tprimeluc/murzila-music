using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MurzilaMusic.App.Services;

namespace MurzilaMusic.App;

public partial class FeaturesWindow : Window
{
    private readonly ObservableCollection<OnlineFeature> _features = new();

    public FeaturesWindow()
    {
        InitializeComponent();
        FeaturesList.ItemsSource = _features;
        Refresh();
        Txt.Changed += Txt_Changed;
        Closed += (_, _) => Txt.Changed -= Txt_Changed;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (Resources["WindowIntro"] is Storyboard intro) intro.Begin(this);
    }

    private void Txt_Changed() => Dispatcher.BeginInvoke(new Action(Refresh));

    private void Refresh()
    {
        string? selected = CbCategory.SelectedItem as string;
        _features.Clear();
        foreach (var feature in Txt.OnlineFeatures) _features.Add(feature);

        CbCategory.ItemsSource = Txt.FeatureCategories;
        CbCategory.SelectedItem = Txt.FeatureCategories.Contains(selected ?? "")
            ? selected
            : Txt.FeaturesAllCategories;

        TbTitle.Text = Txt.FeaturesTitle;
        TbSubtitle.Text = Txt.FeaturesSubtitle;
        TbOnlineBadge.Text = Txt.FeaturesOnlineBadge;
        TbSearch.Tag = Txt.FeaturesSearchPlaceholder;
        BtnClose.Content = Txt.FeaturesClose;
        TbFooter.Text = Txt.FeaturesFooter;
        ApplyFilter();
    }

    private void Category_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        string query = TbSearch.Text.Trim();
        string category = CbCategory.SelectedItem as string ?? Txt.FeaturesAllCategories;

        ICollectionView view = CollectionViewSource.GetDefaultView(FeaturesList.ItemsSource);
        view.Filter = item =>
        {
            if (item is not OnlineFeature feature) return false;
            if (category != Txt.FeaturesAllCategories
                && !string.Equals(feature.Category, category, StringComparison.OrdinalIgnoreCase))
                return false;
            if (query.Length == 0) return true;
            return feature.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || feature.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || feature.Category.Contains(query, StringComparison.OrdinalIgnoreCase);
        };
        view.Refresh();

        int count = view.Cast<OnlineFeature>().Count();
        TbCount.Text = Txt.FeaturesCount(count);
        TbEmpty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (count == 0) TbEmpty.Text = Txt.FeaturesEmpty;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
