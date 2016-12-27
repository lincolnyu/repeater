using Windows.ApplicationModel.Activation;

namespace Repeater
{
    /// <summary>
    /// Implement this interface if your page invokes the file save picker
    /// API
    /// </summary>
    interface IFileSavePickerContinuable
    {
        /// <summary>
        /// This method is invoked when the file save picker returns saved
        /// files
        /// </summary>
        /// <param name="args">Activated event args object that contains returned file from file save picker</param>
        void ContinueFileSavePicker(FileSavePickerContinuationEventArgs args);
    }

}
