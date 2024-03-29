using Azure.Search.Documents.Indexes;
using VectorSearchAiAssistant.SemanticKernel.Models;
using VectorSearchAiAssistant.SemanticKernel.TextEmbedding;

namespace VectorSearchAiAssistant.Service.Models.Search
{
    public class User : EmbeddedEntity
    {
        [EmbeddingField(Label = "User ANme")]
        public string userName { get; set; }
        [EmbeddingField(Label = "User title")]
        public string title { get; set; }
        [SimpleField]
        [EmbeddingField(Label = "User first name")]
        public string firstName { get; set; }
        [SimpleField]
        [EmbeddingField(Label = "User last name")]
        public string lastName { get; set; }
        [SimpleField]
        [EmbeddingField(Label = "User email address")]
        public string emailAddress { get; set; }
        [SimpleField]
        [EmbeddingField(Label = "User phone number")]
        public string phoneNumber { get; set; }
        [SimpleField]
        public string creationDate { get; set; }
        [SimpleField(IsHidden = true)]
        public Password password { get; set; }


        public User(string id, string type, string userName, string title,
            string firstName, string lastName, string emailAddress, string phoneNumber,
            string creationDate, Password password)
        {
            this.id = id;
            this.userName = userName;
            this.title = title;
            this.firstName = firstName;
            this.lastName = lastName;
            this.emailAddress = emailAddress;
            this.phoneNumber = phoneNumber;
            this.creationDate = creationDate;
            this.password = password;
        }
    }

    public class Password
    {
        [SimpleField(IsHidden = true)]
        public string hash { get; set; }
        [SimpleField(IsHidden = true)]
        public string salt { get; set; }

        public Password(string hash, string salt)
        {
            this.hash = hash;
            this.salt = salt;
        }
    }   
}
