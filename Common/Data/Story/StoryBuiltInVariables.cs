using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Virtual, always-present story variables that are never authored and never persisted. There are two:
    /// <see cref="AppGamebook"/> — a read-only variable whose value is the medium currently being rendered
    /// (<see cref="AppValue"/> in the app, <see cref="GamebookValue"/> in the printed book) — and
    /// <see cref="AppTheme"/> — a read-only variable whose value is the theme currently being rendered
    /// (<see cref="DarkValue"/>/<see cref="LightValue"/>, following the App preview's Dark/Light toggle; always
    /// <see cref="LightValue"/> in the always-light Gamebook). Either lets a SmartFormat node branch a text on the
    /// medium/theme (via an ordinary External Variable node) without a full splitter. They surface in the Variables tab
    /// (read-only) and the External Variable node picker, resolve in the renderer, and reserve their names — but they
    /// live outside <see cref="StoryProject.Variables"/>, so they are excluded from persistence, the Gamebook's
    /// section-combination enumeration and the settable Set-external list automatically.
    /// </summary>
    [PublicAPI]
    public static class StoryBuiltInVariables
    {
        /// <summary>The value the <see cref="AppGamebook"/> variable takes when rendering the interactive app.</summary>
        public const string AppValue = "App";

        /// <summary>The value the <see cref="AppGamebook"/> variable takes when rendering the printed gamebook.</summary>
        public const string GamebookValue = "Gamebook";

        /// <summary>The value the <see cref="AppTheme"/> variable takes in the dark theme (the App preview's default).</summary>
        public const string DarkValue = "Dark";

        /// <summary>The value the <see cref="AppTheme"/> variable takes in the light/paper theme (and always in the Gamebook).</summary>
        public const string LightValue = "Light";

        /// <summary>Stable id of the built-in App/Gamebook variable (fixed so External Variable wires to it survive reloads).</summary>
        public static readonly Guid AppGamebookId = new("a9905e00-0000-4000-8000-000000000001");

        /// <summary>Stable id of the built-in App theme variable (fixed so External Variable wires to it survive reloads).</summary>
        public static readonly Guid AppThemeId = new("a9905e00-0000-4000-8000-000000000002");

        /// <summary>
        /// The read-only medium variable. Its <see cref="StoryVariable.Name"/> doubles as the SmartFormat placeholder
        /// token (<c>{AppGamebook}</c>); its value is resolved from the render target, not from the preview values.
        /// </summary>
        public static readonly StoryVariable AppGamebook = new()
        {
            Id             = AppGamebookId,
            Name           = "AppGamebook",
            Description    = "Built-in: the medium being rendered — \"App\" in the app, \"Gamebook\" in the printed book. Read-only; its value follows the preview mode.",
            PossibleValues = new List<string> { AppValue, GamebookValue }
        };

        /// <summary>
        /// The read-only theme variable. Its <see cref="StoryVariable.Name"/> doubles as the SmartFormat placeholder
        /// token (<c>{AppTheme}</c>); its value follows the App preview's Dark/Light toggle and is always
        /// <see cref="LightValue"/> in the printed gamebook.
        /// </summary>
        public static readonly StoryVariable AppTheme = new()
        {
            Id             = AppThemeId,
            Name           = "AppTheme",
            Description    = "Built-in: the theme being rendered — \"Dark\" or \"Light\" following the App preview toggle, always \"Light\" in the printed gamebook. Read-only.",
            PossibleValues = new List<string> { LightValue, DarkValue }
        };

        /// <summary>All built-in variables, in display order.</summary>
        public static readonly IReadOnlyList<StoryVariable> All = new[] { AppGamebook, AppTheme };

        /// <summary>True when <paramref name="id"/> is a built-in (virtual, non-persisted) variable.</summary>
        public static bool IsBuiltIn(Guid id) => id == AppGamebookId || id == AppThemeId;

        /// <summary>The built-in variable with <paramref name="id"/>, or null when it is not a built-in.</summary>
        public static StoryVariable? Find(Guid id) =>
            id == AppGamebookId ? AppGamebook
          : id == AppThemeId    ? AppTheme
          : null;

        /// <summary>True when <paramref name="name"/> matches a built-in variable's name (case-insensitive) — a reserved name.</summary>
        public static bool IsReservedName(string name) =>
            All.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The <see cref="AppGamebook"/> variable's value for <paramref name="target"/>.</summary>
        public static string ValueFor(StoryRenderTarget target) => target == StoryRenderTarget.App ? AppValue : GamebookValue;

        /// <summary>
        /// The current value of the built-in variable <paramref name="id"/> for this render. <see cref="AppGamebook"/>
        /// follows the medium (<paramref name="target"/>); <see cref="AppTheme"/> follows the previewed theme — always
        /// <see cref="LightValue"/> in the Gamebook, else the theme seeded per render into <paramref name="values"/>
        /// (falling back to <see cref="DarkValue"/>, the App preview's default).
        /// </summary>
        public static string ValueFor(Guid id, StoryRenderTarget target, IReadOnlyDictionary<Guid, string> values)
        {
            if (id == AppThemeId)
                return target == StoryRenderTarget.Gamebook
                    ? LightValue
                    : values.TryGetValue(AppThemeId, out string? theme) ? theme : DarkValue;
            return ValueFor(target);
        }
    }
}
