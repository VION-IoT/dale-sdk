using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Vion.Dale.DevHost.Scenarios
{
    /// <summary>
    ///     A parsed <c>*.scenario.json</c> file — the RFC 0006 v1 vocabulary: a tiny, versioned, git-committed
    ///     JSON description of a manual-test scenario (setup, ordered stimuli, watch list, human judgments),
    ///     executed by <see cref="ScenarioRunner" /> over <see cref="Control.IDevHostControl" /> and consumed
    ///     identically by the web UI ("Player"), CI, and agents.
    ///     <para>
    ///         Parsing is strict (<see cref="JsonUnmappedMemberHandling.Disallow" /> — the schema's
    ///         <c>additionalProperties: false</c> posture): evolution is by version bump, not silent extra
    ///         fields. <see cref="Parse" /> throws <see cref="ScenarioFormatException" /> carrying every
    ///         structural error at once.
    ///     </para>
    /// </summary>
    public sealed class ScenarioFile
    {
        /// <summary>The vocabulary version this implementation understands.</summary>
        public const int SupportedVersion = 1;

        internal static readonly JsonSerializerOptions SerializerOptions = new()
                                                                           {
                                                                               PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                                                               UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,

                                                                               // Same strictness posture as Disallow: silent last-wins on a
                                                                               // duplicated key would contradict additionalProperties: false.
                                                                               AllowDuplicateProperties = false,
                                                                           };

        private static readonly Regex IdSlug = new("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);

        // "value": null must stay distinguishable from an absent "value" (null writes null; absent is a
        // format error). Nullable JsonElement can't make that distinction — STJ maps an explicit JSON null
        // to the null value — so JsonElement-typed fields are non-nullable: default(JsonElement) has
        // ValueKind Undefined = absent, an explicit null parses to ValueKind.Null.

        [JsonPropertyName("$schema")]
        public string? Schema { get; init; }

        public int Version { get; init; }

        public string? Id { get; init; }

        public string? Title { get; init; }

        public string? Description { get; init; }

        public string? Topology { get; init; }

        public IReadOnlyList<string>? Specs { get; init; }

        public IReadOnlyList<ScenarioStep>? Setup { get; init; }

        public IReadOnlyList<ScenarioStep>? Steps { get; init; }

        public IReadOnlyList<string>? Watch { get; init; }

        public IReadOnlyList<ScenarioJudgment>? Judge { get; init; }

        /// <summary>
        ///     Parse and structurally validate scenario JSON. Throws <see cref="ScenarioFormatException" />
        ///     listing every problem; name-path/topology resolution against the wired host happens later, at
        ///     run time (<see cref="ScenarioRunner" />).
        /// </summary>
        public static ScenarioFile Parse(string json)
        {
            ScenarioFile? file;
            try
            {
                file = JsonSerializer.Deserialize<ScenarioFile>(json, SerializerOptions);
            }
            catch (JsonException e)
            {
                throw new ScenarioFormatException(new[] { $"not valid scenario JSON: {e.Message}" });
            }

            if (file is null)
            {
                throw new ScenarioFormatException(new[] { "not valid scenario JSON: document is null" });
            }

            var errors = file.StructuralErrors();
            if (errors.Count > 0)
            {
                throw new ScenarioFormatException(errors);
            }

            return file;
        }

        /// <summary>Load and parse a scenario file; the id must match the file name (<c>&lt;id&gt;.scenario.json</c>).</summary>
        public static ScenarioFile Load(string path)
        {
            var file = Parse(File.ReadAllText(path));
            var expectedId = Path.GetFileName(path);
            if (expectedId.EndsWith(ScenarioStore.FileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                expectedId = expectedId.Substring(0, expectedId.Length - ScenarioStore.FileSuffix.Length);
            }

            if (!string.Equals(file.Id, expectedId, StringComparison.Ordinal))
            {
                throw new ScenarioFormatException(new[] { $"id '{file.Id}' does not match the file name (expected '{expectedId}')" });
            }

            return file;
        }

        /// <summary>
        ///     Guard for hand-constructed instances: the runner re-validates whatever it is handed so a
        ///     malformed in-memory file fails loudly instead of NRE-ing mid-run. No-op for parsed files.
        /// </summary>
        internal void EnsureStructurallyValid()
        {
            var errors = StructuralErrors();
            if (errors.Count > 0)
            {
                throw new ScenarioFormatException(errors);
            }
        }

        private List<string> StructuralErrors()
        {
            var errors = new List<string>();

            if (Version != SupportedVersion)
            {
                errors.Add($"version must be {SupportedVersion} (got {Version}) — unknown vocabulary versions are rejected loudly (RFC 0006)");
            }

            if (string.IsNullOrEmpty(Id) || !IdSlug.IsMatch(Id) || Id.Contains(".."))
            {
                errors.Add("id is required and must be a URL-safe slug ([A-Za-z0-9._-], starting alphanumeric, no '..')");
            }
            else if (string.Equals(Id, "schema", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("id 'schema' is reserved (GET /api/scenarios/schema serves the format schema)");
            }

            if (string.IsNullOrEmpty(Topology))
            {
                errors.Add("topology is required — the topology id this scenario expects to run against");
            }

            ValidateSteps(Setup, "setup", true, errors);
            ValidateSteps(Steps, "steps", false, errors);

            for (var i = 0; i < (Watch?.Count ?? 0); i++)
            {
                if (string.IsNullOrWhiteSpace(Watch![i]))
                {
                    errors.Add($"watch[{i}]: empty name path");
                }
            }

            for (var i = 0; i < (Judge?.Count ?? 0); i++)
            {
                if (string.IsNullOrWhiteSpace(Judge![i].Text))
                {
                    errors.Add($"judge[{i}]: text is required");
                }
            }

            return errors;
        }

        private static void ValidateSteps(IReadOnlyList<ScenarioStep>? steps, string section, bool setupOnlyShapes, List<string> errors)
        {
            for (var i = 0; i < (steps?.Count ?? 0); i++)
            {
                foreach (var error in steps![i].StructuralErrors(setupOnlyShapes))
                {
                    errors.Add($"{section}[{i}]: {error}");
                }
            }
        }
    }

    /// <summary>
    ///     One setup entry or step — exactly one of the four closed shapes (<c>set</c>, <c>digitalInput</c> /
    ///     <c>analogInput</c>, <c>waitUntil</c>, <c>wait</c>), each with optional <c>label</c> and <c>spec</c>.
    /// </summary>
    public sealed class ScenarioStep
    {
        public string? Label { get; init; }

        public string? Spec { get; init; }

        public string? Set { get; init; }

        public JsonElement Value { get; init; }

        public ScenarioContractRef? DigitalInput { get; init; }

        public ScenarioContractRef? AnalogInput { get; init; }

        public ScenarioWaitUntil? WaitUntil { get; init; }

        public double? TimeoutSeconds { get; init; }

        public ScenarioWaitStep? Wait { get; init; }

        /// <summary>Which of the closed shapes this step is, for reports and rendering.</summary>
        [JsonIgnore]
        public string Kind
        {
            get
            {
                if (Set is not null)
                {
                    return "set";
                }

                if (DigitalInput is not null)
                {
                    return "digitalInput";
                }

                if (AnalogInput is not null)
                {
                    return "analogInput";
                }

                if (WaitUntil is not null)
                {
                    return "waitUntil";
                }

                return "wait";
            }
        }

        internal IEnumerable<string> StructuralErrors(bool setupOnlyShapes)
        {
            var shapes = 0;
            if (Set is not null)
            {
                shapes++;
            }

            if (DigitalInput is not null)
            {
                shapes++;
            }

            if (AnalogInput is not null)
            {
                shapes++;
            }

            if (WaitUntil is not null)
            {
                shapes++;
            }

            if (Wait is not null)
            {
                shapes++;
            }

            if (shapes != 1)
            {
                yield return "a step is exactly one of set / digitalInput / analogInput / waitUntil / wait";

                yield break;
            }

            if (setupOnlyShapes && (WaitUntil is not null || Wait is not null))
            {
                yield return "setup entries stage state (set / digitalInput / analogInput) — waits belong in steps";

                yield break;
            }

            if (Set is not null)
            {
                if (string.IsNullOrWhiteSpace(Set))
                {
                    yield return "set: empty name path";
                }

                if (Value.ValueKind == JsonValueKind.Undefined)
                {
                    yield return "set requires value (use an explicit null to write null)";
                }
            }
            else if (Value.ValueKind != JsonValueKind.Undefined && DigitalInput is null && AnalogInput is null)
            {
                yield return $"value is not valid on a {Kind} step";
            }

            if (DigitalInput is not null)
            {
                foreach (var error in DigitalInput.StructuralErrors("digitalInput"))
                {
                    yield return error;
                }

                if (Value.ValueKind != JsonValueKind.True && Value.ValueKind != JsonValueKind.False)
                {
                    yield return "digitalInput requires a boolean value";
                }
            }

            if (AnalogInput is not null)
            {
                foreach (var error in AnalogInput.StructuralErrors("analogInput"))
                {
                    yield return error;
                }

                if (Value.ValueKind != JsonValueKind.Number)
                {
                    yield return "analogInput requires a numeric value";
                }
            }

            if (WaitUntil is not null)
            {
                foreach (var error in WaitUntil.StructuralErrors())
                {
                    yield return error;
                }

                if (TimeoutSeconds is <= 0)
                {
                    yield return "timeoutSeconds must be positive";
                }
            }
            else if (TimeoutSeconds is not null)
            {
                yield return "timeoutSeconds is only valid on a waitUntil step";
            }

            if (Wait is not null && Wait.Seconds <= 0)
            {
                yield return "wait.seconds must be positive";
            }
        }
    }

    /// <summary>
    ///     A hardware-contract reference (<c>digitalInput</c> / <c>analogInput</c>): the block name plus its contract
    ///     identifier.
    /// </summary>
    public sealed class ScenarioContractRef
    {
        public string? Block { get; init; }

        public string? Contract { get; init; }

        internal IEnumerable<string> StructuralErrors(string shape)
        {
            if (string.IsNullOrWhiteSpace(Block))
            {
                yield return $"{shape}.block is required";
            }

            if (string.IsNullOrWhiteSpace(Contract))
            {
                yield return $"{shape}.contract is required";
            }
        }
    }

    /// <summary>
    ///     A <c>waitUntil</c> condition: a property name path plus exactly one comparator. Comparison semantics
    ///     per RFC 0006 ("Comparison semantics"): <c>above</c>/<c>below</c> are numeric; <c>equals</c>/
    ///     <c>notEquals</c> are exact (numbers optionally with <c>tolerance</c>, enums by case-sensitive member
    ///     name, <c>null</c> legal); structs/arrays are not comparable in v1.
    /// </summary>
    public sealed class ScenarioWaitUntil
    {
        public string? Property { get; init; }

        public JsonElement Above { get; init; }

        public JsonElement Below { get; init; }

        [JsonPropertyName("equals")]
        public JsonElement EqualTo { get; init; }

        public JsonElement NotEquals { get; init; }

        public double? Tolerance { get; init; }

        internal IEnumerable<string> StructuralErrors()
        {
            if (string.IsNullOrWhiteSpace(Property))
            {
                yield return "waitUntil.property is required";
            }

            var comparators = 0;
            if (Above.ValueKind != JsonValueKind.Undefined)
            {
                comparators++;
                if (Above.ValueKind != JsonValueKind.Number)
                {
                    yield return "waitUntil.above must be a number";
                }
            }

            if (Below.ValueKind != JsonValueKind.Undefined)
            {
                comparators++;
                if (Below.ValueKind != JsonValueKind.Number)
                {
                    yield return "waitUntil.below must be a number";
                }
            }

            if (EqualTo.ValueKind != JsonValueKind.Undefined)
            {
                comparators++;
                if (EqualTo.ValueKind == JsonValueKind.Object || EqualTo.ValueKind == JsonValueKind.Array)
                {
                    yield return "waitUntil.equals does not compare structs/arrays in v1 — a scenario needing that is a C# test";
                }
            }

            if (NotEquals.ValueKind != JsonValueKind.Undefined)
            {
                comparators++;
                if (NotEquals.ValueKind == JsonValueKind.Object || NotEquals.ValueKind == JsonValueKind.Array)
                {
                    yield return "waitUntil.notEquals does not compare structs/arrays in v1 — a scenario needing that is a C# test";
                }
            }

            if (comparators != 1)
            {
                yield return "waitUntil takes exactly one of above / below / equals / notEquals";
            }

            if (Tolerance is not null)
            {
                if (Tolerance < 0)
                {
                    yield return "waitUntil.tolerance must be non-negative";
                }

                if (EqualTo.ValueKind != JsonValueKind.Number)
                {
                    yield return "waitUntil.tolerance is only valid with a numeric equals";
                }
            }
        }
    }

    /// <summary>A fixed pause for stimulus pacing (<c>wait</c>) — shaping inputs over time, never awaiting outcomes.</summary>
    public sealed class ScenarioWaitStep
    {
        public double Seconds { get; init; }
    }

    /// <summary>A human-judgment checklist item — v1 has no auto-asserting checks; CI reports these as <c>requires human</c>.</summary>
    public sealed class ScenarioJudgment
    {
        public string? Text { get; init; }

        public string? Spec { get; init; }
    }

    /// <summary>A scenario file failed structural validation; <see cref="Errors" /> lists every problem at once.</summary>
    public sealed class ScenarioFormatException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public ScenarioFormatException(IReadOnlyList<string> errors) : base(string.Join("; ", errors))
        {
            Errors = errors;
        }
    }
}