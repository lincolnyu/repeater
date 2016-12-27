using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;

namespace Repeater
{
    public class Debugger
    {
        #region Construtors

        public Debugger(int maxMessageLines = 5)
        {
            Messages = new ObservableCollection<string>();
            MaxMessageLines = maxMessageLines;
        }

        #endregion

        #region Proeprties

        public ObservableCollection<string> Messages { get; private set; }

        public int MaxMessageLines { get; set; }

        #endregion

        #region Methods

        public void WriteMessage(string message)
        {
            Debug.WriteLine(message);
            Messages.Add(message);
        }

        public void WriteMessageFormat(string format, params object[] args)
        {
            var message = string.Format(format, args);
            WriteMessage(message);
        }

        public string GetMessageTextToDisplay()
        {
            var sb = new StringBuilder();
            var start = Messages.Count - MaxMessageLines;
            if (start < 0)
            {
                start = 0;
            }
            for (var i = start; i < Messages.Count; i++)
            {
                sb.AppendLine(Messages[i]);
            }
            return sb.ToString();
        }

        public override string ToString()
        {
            return GetMessageTextToDisplay();
        }

        #endregion
    }
}
