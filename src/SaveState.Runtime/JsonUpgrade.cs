using System;
using System.Globalization;
using System.Text.Json.Nodes;

namespace SaveState
{
    /// <summary>
    /// Ready-made conversions for <see cref="FieldMigration.Convert"/> - the "the field changed type" half of
    /// migrating an old save file.
    ///
    /// <para>Every conversion is tolerant: a value that cannot be read (wrong JSON type, unparsable text, a missing
    /// source) becomes the supplied fallback instead of throwing. A migration runs on a player's real save file, so
    /// "this one field was garbled" must not turn into "the save cannot be loaded at all".</para>
    ///
    /// <code>
    /// new FieldMigration("profile.json", fromVersion: 1)
    ///     .Convert("Profile/CoinsText", "Profile/Coins", JsonUpgrade.ToInt())
    ///     .Convert("Profile/PlayTimeSeconds", "Profile/PlayTime", JsonUpgrade.ToText(n =&gt; ...)),
    /// </code>
    /// </summary>
    public static class JsonUpgrade
    {
        /// <summary>Keeps the value as-is (the conversion form of a rename).</summary>
        public static Func<JsonNode?, JsonNode?> Keep()
        {
            return node => node;
        }

        /// <summary>Converts to a JSON number, using <paramref name="fallback"/> when the value cannot be read.</summary>
        public static Func<JsonNode?, JsonNode?> ToNumber(double fallback = 0d)
        {
            return node => JsonValue.Create(ReadNumber(node, fallback));
        }

        /// <summary>Converts to a JSON integer (rounded), using <paramref name="fallback"/> when unreadable.</summary>
        public static Func<JsonNode?, JsonNode?> ToInt(int fallback = 0)
        {
            return node => JsonValue.Create((int)Math.Round(ReadNumber(node, fallback)));
        }

        /// <summary>Converts to a JSON boolean; accepts <c>true</c>/<c>false</c> and the usual 0/1 spellings.</summary>
        public static Func<JsonNode?, JsonNode?> ToBool(bool fallback = false)
        {
            return node =>
            {
                if (node is JsonValue value)
                {
                    if (value.TryGetValue(out bool boolean))
                    {
                        return JsonValue.Create(boolean);
                    }

                    string? text = TextOf(node);
                    if (text != null)
                    {
                        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(text, "1", StringComparison.Ordinal)
                            || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase))
                        {
                            return JsonValue.Create(true);
                        }

                        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(text, "0", StringComparison.Ordinal)
                            || string.Equals(text, "no", StringComparison.OrdinalIgnoreCase))
                        {
                            return JsonValue.Create(false);
                        }
                    }
                }

                return JsonValue.Create(fallback);
            };
        }

        /// <summary>Converts to a JSON string (numbers and booleans are printed as text).</summary>
        public static Func<JsonNode?, JsonNode?> ToText(string fallback = "")
        {
            return node => JsonValue.Create(TextOf(node) ?? fallback);
        }

        /// <summary>
        /// Converts to a JSON string with a host-provided projection (the general "type + format changed" case):
        /// the projection receives the old value as text (or <c>null</c>) and returns the new text.
        /// </summary>
        public static Func<JsonNode?, JsonNode?> ToText(Func<string?, string?> project)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            return node =>
            {
                string? text = project(TextOf(node));
                return text == null ? null : JsonValue.Create(text);
            };
        }

        /// <summary>
        /// Turns a scalar into a single-element array (or keeps an existing array). Handy when a field went from
        /// "one value" to "a list of values".
        /// </summary>
        public static Func<JsonNode?, JsonNode?> ToArray()
        {
            return node =>
            {
                if (node is JsonArray array)
                {
                    return array;
                }

                var wrapped = new JsonArray();
                if (node != null)
                {
                    wrapped.Add(node.DeepClone());
                }

                return wrapped;
            };
        }

        /// <summary>Seconds (number) to a <c>mm:ss</c>-style text, e.g. a play-time field that changed representation.</summary>
        public static Func<JsonNode?, JsonNode?> SecondsToClockText()
        {
            return node =>
            {
                double seconds = ReadNumber(node, 0d);
                if (seconds < 0)
                {
                    seconds = 0;
                }

                var span = TimeSpan.FromSeconds(seconds);
                return JsonValue.Create(
                    ((int)span.TotalMinutes).ToString(CultureInfo.InvariantCulture)
                    + ":"
                    + span.Seconds.ToString("00", CultureInfo.InvariantCulture));
            };
        }

        private static double ReadNumber(JsonNode? node, double fallback)
        {
            if (node is JsonValue value)
            {
                if (value.TryGetValue(out double number))
                {
                    return number;
                }

                if (value.TryGetValue(out int integer))
                {
                    return integer;
                }
            }

            string? text = TextOf(node);
            if (text != null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static string? TextOf(JsonNode? node)
        {
            if (node == null)
            {
                return null;
            }

            if (node is JsonValue value && value.TryGetValue(out string? text))
            {
                return text;
            }

            // Numbers/booleans/objects: their JSON text is the most useful thing to hand to a projection.
            return node.ToJsonString();
        }
    }
}
