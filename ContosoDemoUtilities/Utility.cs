using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;


namespace ContosoUtilities
{
    internal static class Utility
    {
        // string endpopint= ExtractTextBetween(diagnostics.ToString(), "rntbd://", ":");
        public static string ExtractTextBetween(string input, string startMarker, string endMarker)
        {
            // Define a regular expression pattern to match text between startMarker and endMarker
            string pattern = $@"{Regex.Escape(startMarker)}(.*?){Regex.Escape(endMarker)}";

            // Use Regex.Match to find the first match
            Match match = Regex.Match(input, pattern);

            // Check if a match was found
            if (match.Success)
            {
                // Extract the captured group (the text between startMarker and endMarker)
                return match.Groups[1].Value;
            }
            else
            {
                // Return a default value or throw an exception, depending on your requirements
                return "TextNotFound";
            }
        }
    }
}
