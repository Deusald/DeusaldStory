using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using SmartFormat;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Renders a "gamebook condition" localization string for a set of variable values using SmartFormat, so the
    /// editor can preview how the text reads. Placeholders reference variables by name, e.g. <c>{Health}</c>.
    /// </summary>
    [PublicAPI]
    public static class StoryConditionPreview
    {
        /// <summary>
        /// Formats <paramref name="format"/> by substituting <paramref name="values"/> (variable name → value) with
        /// SmartFormat. On any formatting error the raw <paramref name="format"/> is returned and
        /// <paramref name="error"/> carries the message; on success <paramref name="error"/> is null.
        /// </summary>
        public static string Render(string format, IDictionary<string, object> values, out string? error)
        {
            error = null;
            if (string.IsNullOrEmpty(format)) return string.Empty;

            try
            {
                return Smart.Format(format, values);
            }
            catch (Exception e)
            {
                error = e.Message;
                return format;
            }
        }
    }
}
