using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Virtual, always-present story variables that are never authored and never persisted. There are two:
    /// <see cref="Medium"/> — a read-only variable whose value is the medium currently being rendered
    /// (<see cref="AppValue"/> in the app, <see cref="GamebookValue"/> in the printed book) — and
    /// <see cref="Theme"/> — a read-only variable whose value is the theme currently being rendered
    /// (<see cref="DarkValue"/>/<see cref="LightValue"/>, following the App preview's Dark/Light toggle; always
    /// <see cref="LightValue"/> in the always-light Gamebook). Either lets a SmartFormat node branch a text on the
    /// medium/theme (via an ordinary Get Variable node) without a full splitter. They surface in the Variables tab
    /// (read-only, as External + Initial) and the Get Variable node picker, resolve in the renderer, and reserve
    /// their names — but they live outside <see cref="StoryProject.Variables"/>, so they are excluded from
    /// persistence and the Gamebook's section-combination enumeration automatically.
    /// </summary>
    [PublicAPI]
    public static class StoryBuiltInVariables
    {
        /// <summary>The value the <see cref="Medium"/> variable takes when rendering the interactive app.</summary>
        public const string AppValue = "App";

        /// <summary>The value the <see cref="Medium"/> variable takes when rendering the printed gamebook.</summary>
        public const string GamebookValue = "Gamebook";

        /// <summary>The value the <see cref="Theme"/> variable takes in the dark theme (the App preview's default).</summary>
        public const string DarkValue = "Dark";

        /// <summary>The value the <see cref="Theme"/> variable takes in the light/paper theme (and always in the Gamebook).</summary>
        public const string LightValue = "Light";

        /// <summary>Stable id of the built-in Medium variable (fixed so wires to it survive reloads).</summary>
        public static readonly Guid MediumId = new("a9905e00-0000-4000-8000-000000000001");

        /// <summary>Stable id of the built-in Theme variable (fixed so wires to it survive reloads).</summary>
        public static readonly Guid ThemeId = new("a9905e00-0000-4000-8000-000000000002");

        /// <summary>
        /// The read-only medium variable. Its <see cref="StoryVariable.Name"/> doubles as the SmartFormat placeholder
        /// token (<c>{Medium}</c>); its value is resolved from the render target, not from the preview values.
        /// </summary>
        public static readonly StoryVariable Medium = new()
        {
            Id              = MediumId,
            Name            = "Medium",
            Description     = "Built-in: the medium being rendered — \"App\" in the app, \"Gamebook\" in the printed book. Read-only; its value follows the preview mode.",
            Scope           = StoryVariableScope.External,
            ExternalForm    = StoryExternalForm.Initial,
            ExternalSubtype = StoryExternalSubtype.Text,
            TextForm        = StoryTextForm.Options,
            TextOptions     = new List<string> { AppValue, GamebookValue },
            IsReadOnly      = true
        };

        /// <summary>
        /// The read-only theme variable. Its <see cref="StoryVariable.Name"/> doubles as the SmartFormat placeholder
        /// token (<c>{Theme}</c>); its value follows the App preview's Dark/Light toggle and is always
        /// <see cref="LightValue"/> in the printed gamebook.
        /// </summary>
        public static readonly StoryVariable Theme = new()
        {
            Id              = ThemeId,
            Name            = "Theme",
            Description     = "Built-in: the theme being rendered — \"Dark\" or \"Light\" following the App preview toggle, always \"Light\" in the printed gamebook. Read-only.",
            Scope           = StoryVariableScope.External,
            ExternalForm    = StoryExternalForm.Initial,
            ExternalSubtype = StoryExternalSubtype.Text,
            TextForm        = StoryTextForm.Options,
            TextOptions     = new List<string> { LightValue, DarkValue },
            IsReadOnly      = true
        };

        /// <summary>All built-in variables, in display order.</summary>
        public static readonly IReadOnlyList<StoryVariable> All = new[] { Medium, Theme };

        /// <summary>True when <paramref name="id"/> is a built-in (virtual, non-persisted) variable.</summary>
        public static bool IsBuiltIn(Guid id) => id == MediumId || id == ThemeId;

        /// <summary>The built-in variable with <paramref name="id"/>, or null when it is not a built-in.</summary>
        public static StoryVariable? Find(Guid id) =>
            id == MediumId ? Medium
          : id == ThemeId  ? Theme
          : null;

        /// <summary>True when <paramref name="name"/> matches a built-in variable's name (case-insensitive) — a reserved name.</summary>
        public static bool IsReservedName(string name) =>
            All.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The <see cref="Medium"/> variable's value for <paramref name="target"/>.</summary>
        public static string ValueFor(StoryRenderTarget target) => target == StoryRenderTarget.App ? AppValue : GamebookValue;

        /// <summary>
        /// The current value of the built-in variable <paramref name="id"/> for this render. <see cref="Medium"/>
        /// follows the medium (<paramref name="target"/>); <see cref="Theme"/> follows the previewed theme — always
        /// <see cref="LightValue"/> in the Gamebook, else the theme seeded per render into <paramref name="values"/>
        /// (falling back to <see cref="DarkValue"/>, the App preview's default).
        /// </summary>
        public static string ValueFor(Guid id, StoryRenderTarget target, IReadOnlyDictionary<Guid, string> values)
        {
            if (id == ThemeId)
                return target == StoryRenderTarget.Gamebook
                    ? LightValue
                    : values.TryGetValue(ThemeId, out string? theme) ? theme : DarkValue;
            return ValueFor(target);
        }
    }
}
