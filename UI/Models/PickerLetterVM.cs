namespace UI.Models
{
    public class PickerLetterVM : UINotifyPropertyChanged
    {
        private bool _isAvailable;

        public string Letter { get; set; }

        public bool IsAvailable
        {
            get => _isAvailable;
            set
            {
                if (_isAvailable == value)
                {
                    return;
                }

                _isAvailable = value;
                OnPropertyChanged();
            }
        }
    }
}
