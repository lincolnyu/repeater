using Windows.ApplicationModel.Activation;

namespace Repeater
{
    /// <summary>
    /// Implement this interface if your page invokes the file open picker
    /// API.
    /// </summary>
    interface IFileOpenPickerContinuable
    {
        /// <summary>
        /// This method is invoked when the file open picker returns picked
        /// files
        /// </summary>
        /// <param name="args">Activated event args object that contains returned files from file open picker</param>
        void ContinueFileOpenPicker(FileOpenPickerContinuationEventArgs args);
    }
}
