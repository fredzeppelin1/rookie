using System.Threading.Tasks;

namespace AndroidSideloader.Services
{
    /// <summary>
    /// Interface for displaying dialogs and message boxes in a cross-platform manner
    /// </summary>
    public interface IDialogService
    {
        /// <summary>
        /// Shows an information message
        /// </summary>
        Task ShowInfoAsync(string message, string title = "Information");

        /// <summary>
        /// Shows an error message
        /// </summary>
        Task ShowErrorAsync(string message, string title = "Error");

        /// <summary>
        /// Shows a warning message
        /// </summary>
        Task ShowWarningAsync(string message, string title = "Warning");

        /// <summary>
        /// Shows a confirmation dialog with Yes/No buttons
        /// </summary>
        /// <returns>True if user clicked Yes, false if No</returns>
        Task<bool> ShowConfirmationAsync(string message, string title = "Confirm");

        /// <summary>
        /// Shows a confirmation dialog with OK/Cancel buttons
        /// </summary>
        /// <returns>True if user clicked OK, false if Cancel</returns>
        Task<bool> ShowOkCancelAsync(string message, string title = "Confirm");
    }
}
