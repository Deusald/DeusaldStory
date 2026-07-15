using System;
using System.Collections.Generic;
using System.Globalization;

namespace DeusaldStoryCommon
{
    /// <summary>How a value is compared against another in a <see cref="StoryConditionExpr"/> comparison.</summary>
    public enum StoryConditionOperator
    {
        /// <summary>String match (case-insensitive).</summary>
        Equal,

        /// <summary>String mismatch (case-insensitive).</summary>
        NotEqual,

        /// <summary>Both sides parsed to integers — left &lt; right.</summary>
        LessThan,

        /// <summary>Both sides parsed to integers — left &gt; right.</summary>
        GreaterThan,

        /// <summary>Both sides parsed to integers — left ≤ right.</summary>
        LessOrEqual,

        /// <summary>Both sides parsed to integers — left ≥ right.</summary>
        GreaterOrEqual
    }

    /// <summary>Whether a <see cref="StoryConditionExpr"/> node is a leaf comparison or a boolean group of children.</summary>
    public enum StoryConditionExprKind
    {
        Comparison,
        Group
    }

    /// <summary>The boolean operator joining a group's children (applied left-to-right, one connector per group).</summary>
    public enum StoryBoolConnector
    {
        And,
        Or
    }

    /// <summary>Whether a comparison's right-hand side is a typed-in constant or another wired variable.</summary>
    public enum StoryConditionOperandKind
    {
        Constant,
        Variable
    }

    /// <summary>
    /// A boolean expression tree used to auto-resolve a logic node's Choice in the App: each Choice carries one of
    /// these (the locked "Else" choice carries none). A <see cref="StoryConditionExprKind.Comparison"/> node tests a
    /// wired variable against a constant or another wired variable; a <see cref="StoryConditionExprKind.Group"/> node
    /// joins its <see cref="Children"/> with a single <see cref="Connector"/> (nest groups to mix And/Or, e.g.
    /// <c>(a &amp;&amp; b) || (c &amp;&amp; d)</c>). Variables are referenced by the connection <c>FromPoint</c> id feeding the Exit
    /// node's Variables input. Serialized as one flat class (no polymorphism) so Newtonsoft round-trips it cleanly.
    /// </summary>
    public class StoryConditionExpr
    {
        public StoryConditionExprKind Kind { get; set; }

        // ── Comparison ────────────────────────────────────────────────────────
        /// <summary>The wired-variable output id (a connection FromPoint feeding the Exit node's Variables input) on the left.</summary>
        public Guid LeftVariableRef { get; set; }

        /// <summary>Which comparison decides this leaf.</summary>
        public StoryConditionOperator Operator { get; set; }

        /// <summary>Whether the right operand is a constant or another wired variable.</summary>
        public StoryConditionOperandKind RightKind { get; set; }

        /// <summary>The literal compared against, when <see cref="RightKind"/> is Constant.</summary>
        public string RightConstant { get; set; } = string.Empty;

        /// <summary>The wired-variable output id on the right, when <see cref="RightKind"/> is Variable.</summary>
        public Guid RightVariableRef { get; set; }

        // ── Group ─────────────────────────────────────────────────────────────
        /// <summary>The boolean operator joining <see cref="Children"/> (Group only).</summary>
        public StoryBoolConnector Connector { get; set; }

        /// <summary>Sub-expressions of a Group node, evaluated left-to-right and joined by <see cref="Connector"/>.</summary>
        public List<StoryConditionExpr> Children { get; set; } = new();

        /// <summary>A deep copy of this expression tree (so an editor can work on a copy and discard on cancel).</summary>
        public StoryConditionExpr Clone() => new()
        {
            Kind             = Kind,
            LeftVariableRef  = LeftVariableRef,
            Operator         = Operator,
            RightKind        = RightKind,
            RightConstant    = RightConstant,
            RightVariableRef = RightVariableRef,
            Connector        = Connector,
            Children         = Children.ConvertAll(c => c.Clone())
        };

        /// <summary>
        /// Evaluates this expression, resolving each referenced variable's string value via <paramref name="resolve"/>
        /// (given the wired output id). An empty group is vacuously true.
        /// </summary>
        public bool Evaluate(Func<Guid, string> resolve)
        {
            if (Kind == StoryConditionExprKind.Group)
            {
                if (Children.Count == 0) return true;
                bool result = Children[0].Evaluate(resolve);
                for (int x = 1; x < Children.Count; ++x)
                {
                    bool next = Children[x].Evaluate(resolve);
                    result = Connector == StoryBoolConnector.And ? result && next : result || next;
                }
                return result;
            }

            string left  = resolve(LeftVariableRef);
            string right = RightKind == StoryConditionOperandKind.Variable ? resolve(RightVariableRef) : RightConstant;
            return Compare(left, Operator, right);
        }

        /// <summary>
        /// Compares <paramref name="value"/> against <paramref name="other"/> with <paramref name="op"/>. Ordering
        /// operators parse both sides to integers and fail when either isn't a number; Equal / NotEqual compare as
        /// case-insensitive strings.
        /// </summary>
        public static bool Compare(string value, StoryConditionOperator op, string other)
        {
            switch (op)
            {
                case StoryConditionOperator.Equal:    return string.Equals(value, other, StringComparison.OrdinalIgnoreCase);
                case StoryConditionOperator.NotEqual: return !string.Equals(value, other, StringComparison.OrdinalIgnoreCase);
                default:
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int a) ||
                        !int.TryParse(other, NumberStyles.Integer, CultureInfo.InvariantCulture, out int b))
                        return false;
                    return op switch
                    {
                        StoryConditionOperator.LessThan       => a < b,
                        StoryConditionOperator.GreaterThan    => a > b,
                        StoryConditionOperator.LessOrEqual    => a <= b,
                        StoryConditionOperator.GreaterOrEqual => a >= b,
                        _                                     => false
                    };
            }
        }
    }
}
