using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Virtual, always-present story variables that are never authored and never persisted. Today the only one is
    /// <see cref="AppGamebook"/> — a read-only variable whose value is the medium currently being rendered
    /// (<see cref="AppValue"/> in the app, <see cref="GamebookValue"/> in the printed book), so a SmartFormat node can
    /// branch a text on the medium (via an ordinary External Variable node) without a full App/Gamebook Text splitter.
    /// They surface in the Variables tab (read-only) and the External Variable node picker, resolve in the renderer,
    /// and reserve their names — but they live outside <see cref="StoryProject.Variables"/>, so they are excluded from
    /// persistence, the Gamebook's section-combination enumeration and the settable Set-external list automatically.
    /// </summary>
    [PublicAPI]
    public static class StoryBuiltInVariables
    {
        /// <summary>The value the <see cref="AppGamebook"/> variable takes when rendering the interactive app.</summary>
        public const string AppValue = "App";

        /// <summary>The value the <see cref="AppGamebook"/> variable takes when rendering the printed gamebook.</summary>
        public const string GamebookValue = "Gamebook";

        /// <summary>Stable id of the built-in App/Gamebook variable (fixed so External Variable wires to it survive reloads).</summary>
        public static readonly Guid AppGamebookId = new("a9905e00-0000-4000-8000-000000000001");

        /// <summary>
        /// The read-only medium variable. Its <see cref="StoryVariable.Name"/> doubles as the SmartFormat placeholder
        /// token (<c>{AppGamebook}</c>); its value is resolved from the render target, not from the preview values.
        /// </summary>
        public static readonly StoryVariable AppGamebook = new()
        {
            Id             = AppGamebookId,
            Name           = "AppGamebook",
            Description    = "Built-in: the medium being rendered — \"App\" in the app, \"Gamebook\" in the printed book. Read-only; its value follows the preview mode.",
            PossibleValues = new List<string> { AppValue, GamebookValue },
            ConditionKeyId = Guid.Empty
        };

        /// <summary>All built-in variables, in display order.</summary>
        public static readonly IReadOnlyList<StoryVariable> All = new[] { AppGamebook };

        /// <summary>True when <paramref name="id"/> is a built-in (virtual, non-persisted) variable.</summary>
        public static bool IsBuiltIn(Guid id) => id == AppGamebookId;

        /// <summary>The built-in variable with <paramref name="id"/>, or null when it is not a built-in.</summary>
        public static StoryVariable? Find(Guid id) => id == AppGamebookId ? AppGamebook : null;

        /// <summary>True when <paramref name="name"/> matches a built-in variable's name (case-insensitive) — a reserved name.</summary>
        public static bool IsReservedName(string name) =>
            All.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The <see cref="AppGamebook"/> variable's value for <paramref name="target"/>.</summary>
        public static string ValueFor(StoryRenderTarget target) => target == StoryRenderTarget.App ? AppValue : GamebookValue;
    }
}
