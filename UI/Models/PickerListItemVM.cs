using Core.Models;
using System.Windows;

namespace UI.Models
{
    public class PickerListItemVM
    {
        public bool IsSectionHeader { get; set; }
        public string SectionLetter { get; set; }
        public AppModel App { get; set; }

        public Visibility SectionVisibility =>
            IsSectionHeader ? Visibility.Visible : Visibility.Collapsed;

        public Visibility AppVisibility =>
            IsSectionHeader ? Visibility.Collapsed : Visibility.Visible;
    }
}
