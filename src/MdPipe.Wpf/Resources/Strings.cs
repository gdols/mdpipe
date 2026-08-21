using System.Globalization;
using System.Resources;

namespace MdPipe.Wpf.Resources;

/// <summary>
/// The interface text, in whichever language Windows is set to.
/// </summary>
/// <remarks>
/// Generated from Strings.resx and committed rather than produced during the build: WPF compiles
/// XAML through a temporary project that does not see MSBuild-generated sources, which left the
/// class missing for half of every build. Regenerate it whenever the .resx changes.
/// <para>
/// No culture is forced anywhere. <see cref="ResourceManager"/> follows CurrentUICulture, which
/// Windows sets from the display language, so a Spanish machine gets Spanish and the rest get the
/// neutral English.
/// </para>
/// </remarks>
public static class Strings
{
    public static ResourceManager ResourceManager { get; } =
        new("MdPipe.Wpf.Resources.Strings", typeof(Strings).Assembly);

    /// <summary>Left null on purpose so the resource manager uses the machine's UI culture.</summary>
    public static CultureInfo? Culture { get; set; }

    private static string Get(string key) => ResourceManager.GetString(key, Culture) ?? key;

    /// <summary>MdPipe - Markdown Converter</summary>
    public static string WindowTitle => Get("WindowTitle");

    /// <summary>Convert your documents to Markdown. Hassle-free.</summary>
    public static string Tagline => Get("Tagline");

    /// <summary>Reinstall</summary>
    public static string Reinstall => Get("Reinstall");

    /// <summary>Drag your files here</summary>
    public static string DropTitle => Get("DropTitle");

    /// <summary>or click to choose them · PDF, Word, Excel, PowerPoint, HTML, images…</summary>
    public static string DropSubtitle => Get("DropSubtitle");

    /// <summary>No files added yet.</summary>
    public static string NoFilesYet => Get("NoFilesYet");

    /// <summary>Try every file in a folder, not just known formats</summary>
    public static string TryEveryFile => Get("TryEveryFile");

    /// <summary>Useful for files with a wrong or missing extension. The engine decides by what is inside.</summary>
    public static string TryEveryFileTip => Get("TryEveryFileTip");

    /// <summary>What can it convert?</summary>
    public static string WhatCanItConvert => Get("WhatCanItConvert");

    /// <summary>Save to:</summary>
    public static string SaveTo => Get("SaveTo");

    /// <summary>Change…</summary>
    public static string Change => Get("Change");

    /// <summary>Clear list</summary>
    public static string ClearList => Get("ClearList");

    /// <summary>Open folder</summary>
    public static string OpenFolder => Get("OpenFolder");

    /// <summary>Cancel</summary>
    public static string Cancel => Get("Cancel");

    /// <summary>Convert to Markdown</summary>
    public static string ConvertToMarkdown => Get("ConvertToMarkdown");

    /// <summary>About</summary>
    public static string About => Get("About");

    /// <summary>Next to each original file</summary>
    public static string NextToEachOriginal => Get("NextToEachOriginal");

    /// <summary>Starting…</summary>
    public static string Starting => Get("Starting");

    /// <summary>Reinstalling the environment…</summary>
    public static string Reinstalling => Get("Reinstalling");

    /// <summary>Ready · MarkItDown {0}</summary>
    public static string ReadyWithVersion => Get("ReadyWithVersion");

    /// <summary>Converting files…</summary>
    public static string ConvertingFiles => Get("ConvertingFiles");

    /// <summary>Done · {0} file(s) converted</summary>
    public static string DoneCount => Get("DoneCount");

    /// <summary>Cancelled · {0} file(s) converted so far</summary>
    public static string CancelledCount => Get("CancelledCount");

    /// <summary>Finished with warnings · {0}/{1} converted</summary>
    public static string FinishedWithWarnings => Get("FinishedWithWarnings");

    /// <summary>· {0} renamed to avoid overwriting</summary>
    public static string RenamedNote => Get("RenamedNote");

    /// <summary>Skipped 1 folder you do not have permission to read.</summary>
    public static string SkippedFolderOne => Get("SkippedFolderOne");

    /// <summary>Skipped {0} folders you do not have permission to read.</summary>
    public static string SkippedFolderMany => Get("SkippedFolderMany");

    /// <summary>Python is missing. Install Python 3.10 or later and reopen the app.</summary>
    public static string PythonMissingStatus => Get("PythonMissingStatus");

    /// <summary>Python missing</summary>
    public static string PythonMissingTitle => Get("PythonMissingTitle");

    /// <summary>MdPipe needs Python 3.10 or later installed on the system. Download it for free from py...</summary>
    public static string PythonMissingBody => Get("PythonMissingBody");

    /// <summary>Couldn't finish setting up MarkItDown.</summary>
    public static string SetupUnfinishedStatus => Get("SetupUnfinishedStatus");

    /// <summary>Setup couldn't finish</summary>
    public static string SetupUnfinishedTitle => Get("SetupUnfinishedTitle");

    /// <summary>MdPipe couldn't finish its first-time setup. {0} The first run needs internet to downlo...</summary>
    public static string SetupUnfinishedBody => Get("SetupUnfinishedBody");

    /// <summary>Couldn't prepare MarkItDown.</summary>
    public static string PrepareFailedStatus => Get("PrepareFailedStatus");

    /// <summary>Error preparing MdPipe</summary>
    public static string PrepareFailedTitle => Get("PrepareFailedTitle");

    /// <summary>Couldn't finish setup.</summary>
    public static string SetupFailedStatus => Get("SetupFailedStatus");

    /// <summary>MdPipe couldn't finish setting up. {0}</summary>
    public static string SetupFailedBody => Get("SetupFailedBody");

    /// <summary>An unexpected error occurred: {0}</summary>
    public static string UnexpectedErrorBody => Get("UnexpectedErrorBody");

    /// <summary>Pending</summary>
    public static string StatePending => Get("StatePending");

    /// <summary>Converting…</summary>
    public static string StateConverting => Get("StateConverting");

    /// <summary>Converted</summary>
    public static string StateConverted => Get("StateConverted");

    /// <summary>Error: {0}</summary>
    public static string StateError => Get("StateError");

    /// <summary>Choose the files to convert</summary>
    public static string ChooseFilesTitle => Get("ChooseFilesTitle");

    /// <summary>Supported documents</summary>
    public static string SupportedDocuments => Get("SupportedDocuments");

    /// <summary>All files</summary>
    public static string AllFiles => Get("AllFiles");

    /// <summary>Choose where to save the Markdown files</summary>
    public static string ChooseOutputTitle => Get("ChooseOutputTitle");

    /// <summary>About MdPipe</summary>
    public static string AboutTitle => Get("AboutTitle");

    /// <summary>DEVELOPED BY</summary>
    public static string DevelopedBy => Get("DevelopedBy");

    /// <summary>Web</summary>
    public static string Web => Get("Web");

    /// <summary>Email</summary>
    public static string Email => Get("Email");

    /// <summary>Project</summary>
    public static string Project => Get("Project");

    /// <summary>Close</summary>
    public static string Close => Get("Close");

    /// <summary>Version {0}</summary>
    public static string VersionLabel => Get("VersionLabel");

    /// <summary>MdPipe is an independent, unofficial project. It is not affiliated with or sponsored by...</summary>
    public static string Disclaimer => Get("Disclaimer");

    /// <summary>What MdPipe can convert</summary>
    public static string FormatsTitle => Get("FormatsTitle");

    /// <summary>Read from MarkItDown {0}, the engine installed on this computer.</summary>
    public static string FormatsFromEngine => Get("FormatsFromEngine");

    /// <summary>The formats MdPipe ships knowing about. Once it finishes setting itself up, this list c...</summary>
    public static string FormatsFromBundle => Get("FormatsFromBundle");

    /// <summary>{0} formats.</summary>
    public static string FormatsCount => Get("FormatsCount");

    /// <summary>Dragging a file in converts it whatever its extension, so this list only limits what ge...</summary>
    public static string FormatsNote => Get("FormatsNote");
}
