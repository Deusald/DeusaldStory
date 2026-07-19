namespace DeusaldStoryCommon
{
    /// <summary>
    /// What a connection port carries, so wiring only joins compatible ports.
    /// <list type="bullet">
    /// <item><see cref="Flow"/> — plain container-level story flow (go to the next node). Many-in, one-out.</item>
    /// <item><see cref="VFlow"/> — variable-flow: a single container-level path that carries a set of named variables
    /// to a consuming node's "accept variables" input. Many-in, one-out.</item>
    /// <item><see cref="LFlow"/> — the inner logic-render chain inside a logic node. One-in, one-out (a linear chain).</item>
    /// <item><see cref="Text"/> — resolved localization / SmartFormat text.</item>
    /// <item><see cref="Icon"/> — a project image.</item>
    /// <item><see cref="Variable"/> — an App-only variable value (unknown when the Gamebook is printed).</item>
    /// <item><see cref="CVariable"/> — a constant variable, known before any section is evaluated. A CVariable output
    /// may feed a Variable input, but a Variable output may not feed a CVariable input.</item>
    /// </list>
    /// Lives in Common so persisted graph data can carry a port type.
    /// </summary>
    public enum PortType
    {
        Flow,
        VFlow,
        LFlow,
        Text,
        Icon,
        Variable,
        CVariable,

        /// <summary>A logic portal's <b>in</b> port — accepts any value signal (Text / Icon / Variable / Constant). A
        /// portal <b>out</b> adopts the concrete type of whatever is wired into the in; it stays <c>Data</c> until then.</summary>
        Data
    }
}
