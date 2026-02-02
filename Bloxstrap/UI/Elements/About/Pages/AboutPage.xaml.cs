using Bloxstrap.UI.ViewModels.About;

using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Bloxstrap.UI.Elements.About.Pages
{
    /// <summary>
    /// Interaction logic for AboutPage.xaml
    /// </summary>
    public partial class AboutPage
    {
        public AboutPage()
        {
            DataContext = new AboutViewModel();
            InitializeComponent();
        }
    }
}
