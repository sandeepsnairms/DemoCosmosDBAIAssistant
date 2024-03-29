using Azure.Search.Documents.Indexes;
using VectorSearchAiAssistant.SemanticKernel.Models;
using VectorSearchAiAssistant.SemanticKernel.TextEmbedding;

namespace VectorSearchAiAssistant.Service.Models.Search
{

    public class Guide : EmbeddedEntity
    {
        [SearchableField(IsFilterable = true, IsFacetable = true)]
        [EmbeddingField(Label = "Category name")]
        public string category { get; set; }
        [SimpleField]
        [EmbeddingField(Label = "Difficulty Level")]
        public string difficulty { get; set; }
        [SimpleField]
        [EmbeddingField(Label = "Guide Title")]
        public string title { get; set; }
        [SimpleField]
        [EmbeddingField(Label = "Guide Applicable OS")]
        public string applicableOS { get; set; }
        [SimpleField]
        [EmbeddingField(Label = "Guide Known Issues")]
        public string knownIssues { get; set; }
        [SimpleField]
        [EmbeddingField(Label = "Steps to follow")]
        public string steps { get; set; }

        public Guide(string id, string title, string category, string difficulty, string applicableOS, string knownIssues, string steps)
        {
            this.id = id;
            this.category = category;
            this.difficulty = difficulty;
            this.title = title;
            this.applicableOS = applicableOS;
            this.knownIssues = knownIssues;
            this.steps = steps;
        }

        public Guide()
        {
        }
    }

}
