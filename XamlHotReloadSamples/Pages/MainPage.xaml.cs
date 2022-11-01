using XamlHotReload;

namespace XamlHotReloadSamples.Pages;

public partial class MainPage
{
    int count = 0;

    public MainPage()
    {
        InitializeComponent();
        // Content = new Label{
        //     Text="Updated 5"
        // };
    }

    private void OnCounterClicked(object sender, EventArgs e)
    {
        count++;

        if (count == 1)
            CounterBtn.Text = $"Clicked {count} time";
        else
            CounterBtn.Text = $"Clicked {count} times";
    }

    private void Page2Clicked(object sender, EventArgs e)
    {
        Navigation.PushAsync(new MainPage2());
    }
}