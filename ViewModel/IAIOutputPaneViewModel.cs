using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.ViewModel
{
    /// <summary>ViewModel for the IAI output pane, exposing the model's text output message for display.</summary>
    public class IAIOutputPaneViewModel : ViewModelBase
    {
        private string _iaiOutputMessage;
        public string IAIOutputMessage
        {
            get => _iaiOutputMessage;
            set => SetProperty(ref _iaiOutputMessage, value);
        }
    }
}
