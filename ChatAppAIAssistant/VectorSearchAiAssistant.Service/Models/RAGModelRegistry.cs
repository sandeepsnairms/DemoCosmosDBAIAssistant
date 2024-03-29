using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VectorSearchAiAssistant.Service.Models.Search;

namespace VectorSearchAiAssistant.Service.Models
{
    public class RAGModelRegistry
    {
        public static Dictionary<string, RAGModelRegistryEntry> Models = new Dictionary<string, RAGModelRegistryEntry>
            {               
                { 
                    nameof(Guide),
                    new RAGModelRegistryEntry 
                    { 
                        Type = typeof(Guide),
                        TypeMatchingProperties = new List<string> { "steps","applicableOS","category","title" },
                        NamingProperties = new List<string> { "title" }
                    } 
                },
                {
                    nameof(ShortTermMemory),
                    new RAGModelRegistryEntry
                    {
                        Type = typeof(ShortTermMemory),
                        TypeMatchingProperties = new List<string> { "memory__" },
                        NamingProperties = new List<string>()
                    }
                }
            };

        public static RAGModelRegistryEntry? IdentifyType(JObject obj)
        {
            var objProps = obj.Properties().Select(p => p.Name);

            var result = RAGModelRegistry
                .Models
                .Select(m => m.Value)
                .SingleOrDefault(x => objProps.Intersect(x.TypeMatchingProperties).Count() == x.TypeMatchingProperties.Count());

            return result;
        }
    }

    public class RAGModelRegistryEntry
    {
        public Type? Type { get; init; }
        public List<string>? TypeMatchingProperties { get; init; }
        public List<string>? NamingProperties { get; init; }
    }
}
