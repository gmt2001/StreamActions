﻿/*
 * Copyright © 2019-2022 StreamActions Team
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *  http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Text;

namespace StreamActions.Common
{
    /// <summary>
    /// Utility Members.
    /// </summary>
    public static class Util
    {
        #region Public Methods

        /// <summary>
        /// Builds a <see cref="Uri"/>, escaping query and fragment parameters.
        /// </summary>
        /// <param name="baseUri">The base uri.</param>
        /// <param name="queryParams">The query parameters.</param>
        /// <param name="fragmentParams">The fragment parameters.</param>
        /// <returns>A <see cref="Uri"/> with all query and fragment parameters escaped and appended.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="baseUri"/> is null.</exception>
        public static Uri BuildUri(Uri baseUri, IEnumerable<KeyValuePair<string, IEnumerable<string>>>? queryParams = null, IEnumerable<KeyValuePair<string, IEnumerable<string>>>? fragmentParams = null)
        {
            if (baseUri is null)
            {
                throw new ArgumentNullException(nameof(baseUri));
            }

            StringBuilder relativeUri = new();

            if (queryParams is not null && queryParams.Any())
            {
                _ = relativeUri.Append('?');

                bool first = true;
                foreach (KeyValuePair<string, IEnumerable<string>> kvp in queryParams)
                {
                    if (!first)
                    {
                        _ = relativeUri.Append('&');
                    }
                    else
                    {
                        first = false;
                    }

                    foreach (string value in kvp.Value)
                    {
                        _ = relativeUri.Append(Uri.EscapeDataString(kvp.Key)).Append('=').Append(Uri.EscapeDataString(value));
                    }
                }
            }

            if (fragmentParams is not null && fragmentParams.Any())
            {
                _ = relativeUri.Append('#');

                bool first = true;
                foreach (KeyValuePair<string, IEnumerable<string>> kvp in fragmentParams)
                {
                    if (!first)
                    {
                        _ = relativeUri.Append('&');
                    }
                    else
                    {
                        first = false;
                    }

                    foreach (string value in kvp.Value)
                    {
                        _ = relativeUri.Append(Uri.EscapeDataString(kvp.Key)).Append('=').Append(Uri.EscapeDataString(value));
                    }
                }
            }

            return new Uri(baseUri, relativeUri.ToString());
        }

        /// <summary>
        /// Attempts to convert an ISO8601-formatted timestamp into a <see cref="DateTime"/>.
        /// </summary>
        /// <param name="iso8601">An ISO8601-formatter timestamp.</param>
        /// <returns>A <see cref="DateTime"/> on success; <c>null</c> on failure.</returns>
        public static DateTime? ISO8601ToDateTime(string iso8601) => DateTime.TryParseExact(iso8601, "yyyyMMddTHH:mm:ss.FFFZ", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime result1)
                ? result1
                : DateTime.TryParseExact(iso8601, "yyyyMMddTHHmmss.FFFZ", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime result2)
                    ? result2
                    : DateTime.TryParseExact(iso8601, "yyyy-MM-ddTHH:mm:ss.FFFZ", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime result3)
                                    ? result3
                                    : DateTime.TryParseExact(iso8601, "yyyy-MM-ddTHHmmss.FFFZ", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime result4)
                                                    ? result4
                                                    : null;

        #endregion Public Methods
    }
}