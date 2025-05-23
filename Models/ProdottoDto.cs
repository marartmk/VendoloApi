namespace VendoloApi.Models
{
    public class ProdottoDto
    {
        public int id { get; set; }
        public string ProductId { get; set; }
        public string nome { get; set; }
        public int categoria_id { get; set; }
        public decimal Prezzo { get; set; }
        public string Famiglia_Id { get; set; }
    }

}