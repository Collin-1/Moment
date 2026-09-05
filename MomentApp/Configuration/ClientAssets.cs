namespace MomentApp.Configuration;

/// <summary>
/// The room client's module and stylesheet graph, in one place.
/// </summary>
/// <remarks>
/// This list is the single source for the import map, the stylesheet links and — once the
/// service worker lands — its precache list. Keeping them derived from one array is what stops
/// those three drifting apart.
///
/// Every module is referenced with a <c>?v=</c> build stamp. That is deliberate rather than
/// relying on .NET's asset fingerprinting: a relative <c>import './state.js'</c> resolves
/// against the importing module's URL and drops any fingerprint, so a fingerprinted entry
/// point would pull unfingerprinted dependencies, and changing a dependency would not change
/// the entry's fingerprint. A shared stamp invalidates the whole graph at once.
/// </remarks>
public static class ClientAssets
{
    /// <summary>Bare specifier -> path, for the room page's import map.</summary>
    public static readonly IReadOnlyDictionary<string, string> RoomModules = new Dictionary<string, string>
    {
        ["moment/config"] = "/js/room/config.js",
        ["moment/dom"] = "/js/room/dom.js",
        ["moment/state"] = "/js/room/state.js",
        ["moment/hub"] = "/js/room/hub-client.js",
        ["moment/rtc/peers"] = "/js/room/rtc/peer-connections.js",
        ["moment/rtc/media"] = "/js/room/rtc/media-controller.js",
        ["moment/rtc/ice"] = "/js/room/rtc/ice-config.js",
        ["moment/ui/toast"] = "/js/room/ui/toast.js",
        ["moment/ui/timer"] = "/js/room/ui/timer.js",
        ["moment/ui/chat"] = "/js/room/ui/chat-view.js",
        ["moment/ui/participants"] = "/js/room/ui/participants-view.js",
        ["moment/ui/vote"] = "/js/room/ui/vote-panel.js",
        ["moment/ui/call-controls"] = "/js/room/ui/call-controls.js",
        ["moment/ui/video-stage"] = "/js/room/ui/video-stage.js",
    };

    public const string RoomEntry = "/js/room/main.js";

    /// <summary>
    /// The room page's import map, serialised ready for a single @Html.Raw in the view.
    /// </summary>
    /// <remarks>
    /// Built here rather than looped over in Razor: a JSON object literal written inline in a
    /// .cshtml script block is ambiguous to the Razor parser, which silently swallows the
    /// whole element.
    /// </remarks>
    /// <summary>
    /// The complete import-map script element, ready to emit with a single @Html.Raw.
    /// </summary>
    /// <remarks>
    /// Emitted as a whole element rather than written as markup in the view because .NET 9
    /// ships an ImportMapTagHelper that claims <c>script[type=importmap]</c> and rewrites its
    /// content from the static-asset resource collection. Ours is hand-authored, so the helper
    /// replaced it with nothing — silently, with no build warning. Handing Razor an opaque
    /// string keeps the element out of the tag-helper pipeline entirely.
    /// </remarks>
    public static string RoomImportMapScript() =>
        "<script type=\"importmap\">" + RoomImportMapJson() + "</" + "script>";

    public static string RoomImportMapJson() =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            imports = RoomModules.ToDictionary(m => m.Key, m => Versioned(m.Value))
        });

    public static string Versioned(string path) => $"{path}?v={BuildId.Value}";
}
