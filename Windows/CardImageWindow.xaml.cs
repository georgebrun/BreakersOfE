using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BreakersOfE.Windows
{
    public partial class CardImageWindow : Window
    {
        public CardImageWindow(ImageSource? image, string cardName)
        {
            InitializeComponent();
            CardImage.Source = image;
            CardNameText.Text = cardName;
            Title = cardName;
        }

        private void CardImage_Click(object sender, MouseButtonEventArgs e)
            => Close();
    }
}