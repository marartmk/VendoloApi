using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Configuration;
using System.Data.SqlClient;
using System.Data;
using System.Web.UI.WebControls;
using VendoloApi.Models;
using System.Web.Http.Cors;
using System.IO;
using System.Web.UI;

namespace VendoloApi.Controllers
{
    [EnableCors(origins: "http://localhost:5173", headers: "*", methods: "*")]
    public class RistoranteController : ApiController
    {
        private static readonly HttpClient client = new HttpClient();

        private static readonly string WhrApiKey = "";

        // POST: api/login
        [HttpPost]
        [Route("api/test/login")]
        public async Task<IHttpActionResult> Login([FromBody] LoginRequest data)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("api-key", WhrApiKey);

                string jsonPayload = JsonConvert.SerializeObject(new
                {
                    email = data.Email,
                    password = new { value = data.Password }
                });

                StringContent postData = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await client.PostAsync("https://sandbox.weavr.io/multi/login_with_password", postData);
                string responseText = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"🔹 Response Status: {response.StatusCode}");
                Console.WriteLine($"🔹 Response Text: {responseText}");

                if (response.IsSuccessStatusCode)
                {
                    var whrLoginResponse = JsonConvert.DeserializeObject<LoginResponse>(responseText);
                    return Ok(whrLoginResponse);
                }
                else
                {
                    return Content(response.StatusCode, new { message = "Errore nel login", details = responseText });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Errore di connessione al server: {ex.Message}");
                return InternalServerError(new Exception("Errore di connessione al server", ex));
            }
        }

        // POST: api/test/getTavoliSala
        [HttpPost]
        [Route("api/test/getTavoliSala")]
        public async Task<IHttpActionResult> GetTavoliSala([FromBody] TavoliSalaRequest data)
        {
            if (string.IsNullOrWhiteSpace(data.IdCompany))
                return Content(HttpStatusCode.BadRequest, new { message = "IdCompany mancante" });

            string SQLstr = ConfigurationManager.ConnectionStrings["CloudConnection"].ConnectionString;
            List<Tavolo> tavoli = new List<Tavolo>();

            string sqlQuery = @"SELECT 
                            TavoliSala.IdTavolo,
                            TavoliSala.NomeTavolo,
                            StatiTavoli.Descrizione AS StatoDescrizione,    
                            TavoliSala.NumeroTavolo,
                            calendarioTavoli.Descrizione AS CalendarioDescrizione
                        FROM 
                            TavoliSala
                        LEFT JOIN 
                            calendarioTavoli ON TavoliSala.IdTavolo = calendarioTavoli.IdTavolo
                        LEFT JOIN 
                            StatiTavoli ON TavoliSala.Stato = StatiTavoli.Stato
                        WHERE 
                            TavoliSala.IdCompany = @IdCompany";

            try
            {
                using (SqlConnection conn = new SqlConnection(SQLstr))
                {
                    await conn.OpenAsync();
                    using (SqlCommand cmd = new SqlCommand(sqlQuery, conn))
                    {
                        cmd.Parameters.Add("@IdCompany", SqlDbType.UniqueIdentifier).Value = new Guid(data.IdCompany);

                        using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                tavoli.Add(new Tavolo
                                {
                                    Id = reader["IdTavolo"].ToString(),
                                    Nome = reader["NomeTavolo"].ToString(),
                                    Stato = reader["StatoDescrizione"].ToString().ToLower(),
                                    Numero = reader["NumeroTavolo"].ToString(),
                                    Dettagli = reader["CalendarioDescrizione"] == DBNull.Value ? "" : reader["CalendarioDescrizione"].ToString()
                                });
                            }
                        }
                    }
                }

                return Ok(new { success = true, tavoli = tavoli });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Errore GetTavoliSala: {ex.Message}");
                return InternalServerError(new Exception("Errore nel recupero dei tavoli", ex));
            }
        }

        // POST: api/test/getFamiglieByCategoria
        [HttpPost]
        [Route("api/test/getFamiglieByCategoria")]
        public IHttpActionResult GetFamiglieByCategoria([FromBody] CategoriaRequest data)
        {
            if (string.IsNullOrWhiteSpace(data.IdCategoria))
                return Content(HttpStatusCode.BadRequest, new { message = "IdCategoria mancante" });

            string SQLstr = ConfigurationManager.ConnectionStrings["CloudConnection"].ConnectionString;
            List<FamigliaDto> famiglie = new List<FamigliaDto>();

            string sqlQuery = @"SELECT IdFamiglia, Descrizione
                        FROM FamiglieShop
                        WHERE IdCategoria = @IdCategoria";

            try
            {
                using (SqlConnection conn = new SqlConnection(SQLstr))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(sqlQuery, conn))
                    {
                        cmd.Parameters.Add("@IdCategoria", SqlDbType.UniqueIdentifier).Value = new Guid(data.IdCategoria);

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                famiglie.Add(new FamigliaDto
                                {
                                    IdFamiglia = reader["IdFamiglia"].ToString(),
                                    Descrizione = reader["Descrizione"].ToString()
                                });
                            }
                        }
                    }
                }

                return Ok(new { success = true, famiglie = famiglie });
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Errore GetFamiglieByCategoria: " + ex.Message);
                return InternalServerError(new Exception("Errore durante il recupero delle famiglie", ex));
            }
        }

        // POST: api/test/getProdottiRaggruppatiPerFamiglia
        [HttpPost]
        [Route("api/test/getMenuCompletoPerFamiglia")]
        public IHttpActionResult GetMenuCompletoPerFamiglia([FromBody] CategoriaCompanyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.IdCompany) || string.IsNullOrWhiteSpace(request.IdCategoria))
                return Content(HttpStatusCode.BadRequest, new { success = false, message = "IdCompany o IdCategoria mancanti" });

            string connStr = ConfigurationManager.ConnectionStrings["CloudConnection"].ConnectionString;

            string sqlFamiglie = @"
        SELECT IdFamiglia, Descrizione 
        FROM FamiglieShop 
        WHERE IdCategoria = @IdCategoria";

            string sqlPiatti = @"
        SELECT Id, ProductId, Descrizione AS Nome, PrezzoVendita, IdFamiglia 
        FROM ShopData 
        WHERE IdCompany = @IdCompany 
          AND IdCategoria = @IdCategoria 
          AND isDeleted = 0 
          AND isCompleted = 1";

            var famiglie = new Dictionary<string, string>(); // IdFamiglia -> NomeFamiglia
            var result = new Dictionary<string, List<ProdottoDto>>();

            try
            {
                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    conn.Open();

                    // Step 1: Leggi le famiglie della categoria
                    using (SqlCommand cmd = new SqlCommand(sqlFamiglie, conn))
                    {
                        cmd.Parameters.Add("@IdCategoria", SqlDbType.UniqueIdentifier).Value = new Guid(request.IdCategoria);
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string id = reader["IdFamiglia"].ToString();
                                string descrizione = reader["Descrizione"].ToString();
                                famiglie[id] = descrizione;
                                result[descrizione] = new List<ProdottoDto>();
                            }
                        }
                    }

                    // Step 2: Leggi i piatti disponibili
                    using (SqlCommand cmd = new SqlCommand(sqlPiatti, conn))
                    {
                        cmd.Parameters.Add("@IdCompany", SqlDbType.UniqueIdentifier).Value = new Guid(request.IdCompany);
                        cmd.Parameters.Add("@IdCategoria", SqlDbType.UniqueIdentifier).Value = new Guid(request.IdCategoria);
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string idFamiglia = reader["IdFamiglia"].ToString();
                                if (famiglie.ContainsKey(idFamiglia))
                                {
                                    var piatto = new ProdottoDto
                                    {
                                        id = Convert.ToInt32(reader["Id"]),
                                        ProductId = reader["ProductId"].ToString(),
                                        nome = reader["Nome"].ToString(),
                                        categoria_id = 1,
                                        Prezzo = reader["PrezzoVendita"] != DBNull.Value ? Convert.ToDecimal(reader["PrezzoVendita"]) : 0,
                                        Famiglia_Id = idFamiglia
                                    };

                                    string nomeFamiglia = famiglie[idFamiglia];
                                    result[nomeFamiglia].Add(piatto);
                                }
                            }
                        }
                    }
                }

                return Ok(new { success = true, menu = result });
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Errore GetMenuCompletoPerFamiglia: " + ex.Message);
                return InternalServerError(new Exception("Errore durante il recupero del menu", ex));
            }
        }

        // POST: api/test/creaComandaTavolo
        [HttpPost]
        [Route("api/test/creaComandaTavolo")]
        public IHttpActionResult CreaComanda([FromBody] ComandaDto comanda)
        {
            if (comanda == null)
                return BadRequest("Dati comanda non validi");

            var bodyJson = Newtonsoft.Json.JsonConvert.SerializeObject(comanda, Newtonsoft.Json.Formatting.Indented);

            System.Diagnostics.Debug.WriteLine("🔹 JSON ricreato dall'oggetto ComandaDto:");
            System.Diagnostics.Debug.WriteLine(bodyJson);

            List<PiattoDto> piatti = comanda.Piatti;

            string idTavolo = comanda.tavoloId.ToString();
            int coperti = comanda.coperti;
            //string idCameriere = comanda.CameriereId.ToString();
            int idCameriere = comanda.cameriereId;
            string nomeTavolo = "Nome Tavolo"; // da inserire nome tavolo da recupero dati tabella tavoli
            string codicePiatto = "Codice Piatto"; // da inserire codice piatto da recuperare in tabella piatti

            string descrizionePiatto = string.Empty;
            int qta = 1;
            decimal prezzo = 0;
            string idProdotto = "00000000-0000-0000-0000-000000000000";
            string idCompany = comanda.companyId.ToString();
            DateTime dataOrdine = DateTime.Now;
            string oraOrdine = dataOrdine.ToString("HH:mm:ss");
            string idOrdine = Guid.NewGuid().ToString();

            if (string.IsNullOrEmpty(idCompany))
            {
                string messageTxt = "Errore CRCOM1 : IdCompany mancante";
                return BadRequest(messageTxt);
            }

            if (string.IsNullOrEmpty(idTavolo))
            {
                string messageTxt = "Nessun numero tavolo ricevuto";
                return BadRequest(messageTxt);
            }

            bool isProdPresent = IsProductPresent(idTavolo, idProdotto, idCompany);
            string tableOpenOrder = TableOpenOrder(idTavolo, idCompany);
            bool isOpenOrder = false;

            if (!string.IsNullOrEmpty(tableOpenOrder))
            {
                idOrdine = tableOpenOrder;
                isOpenOrder = true;
            }

            string SQLstr = ConfigurationManager.ConnectionStrings["CloudConnection"].ConnectionString;
            SqlConnection SqlConnAcc = new SqlConnection(SQLstr);
            SqlConnAcc.Open();

            SqlCommand cmd2 = new SqlCommand();
            cmd2.Connection = SqlConnAcc;
            cmd2.CommandType = CommandType.Text;


            cmd2.Parameters.Add("@IdCompany", SqlDbType.UniqueIdentifier).Value = new Guid(idCompany);
            cmd2.Parameters.Add("@IdTavolo", SqlDbType.UniqueIdentifier).Value = new Guid(idTavolo);
            cmd2.Parameters.Add("@IdOrdine", SqlDbType.UniqueIdentifier).Value = new Guid(idOrdine);
            cmd2.Parameters.Add("@NomeTavolo", SqlDbType.VarChar).Value = nomeTavolo; ;
            cmd2.Parameters.Add("@Importo", SqlDbType.Decimal).Value = prezzo;
            cmd2.Parameters.Add("@DataOrdine", SqlDbType.DateTime).Value = dataOrdine;
            cmd2.Parameters.Add("@OraOrdine", SqlDbType.Time).Value = oraOrdine;
            cmd2.Parameters.Add("@Stato", SqlDbType.Int).Value = 1;
            cmd2.Parameters.Add("@IsDeleted", SqlDbType.Bit).Value = false;

            string strInsertQuery = @"INSERT INTO [dbo].[resturant_orders]
									   ([IdCompany]
                                       ,[IdTavolo]									   
                                       ,[IdOrdine]									   
									   ,[Importo]
                                       ,[Nometavolo]                                       
                                       ,[DataOrdine]
                                       ,[OraOrdine]
									   ,[Stato]
                                       ,[IsDeleted])
								 VALUES
									   (@IdCompany
                                       ,@IdTavolo									   
                                       ,@IdOrdine									  
									   ,@Importo
                                       ,@NomeTavolo                                       
                                       ,@DataOrdine
                                       ,@OraOrdine
									   ,@Stato
                                       ,@IsDeleted)";

            string strUpdateQuery = @"UPDATE resturant_orders SET
                                             IdCompany = @IdCompany,
                                             IdTavolo = @IdTavolo,                                             
                                             IdOrdine = @IdOrdine,                                            
                                             Importo = @Importo,
                                             NomeTavolo = @NomeTavolo,                                            
                                             DataOrdine = @DataOrdine,
                                             OraOrdine = @OraOrdine,
                                             Stato = @Stato,
                                             IsDeleted = @IsDeleted
                                        WHERE
                                             IdOrdine = @IdOrdine AND
                                             IdCompany = @IdCompany";


            string strCommandQuery = string.Empty;

            if (isOpenOrder == true)
            {
                strCommandQuery = strUpdateQuery;
            }
            else
            {
                strCommandQuery = strInsertQuery;
            }

            cmd2.CommandText = strCommandQuery;

            try
            {
                cmd2.ExecuteNonQuery();
                cmd2.Parameters.Clear();

                InserisciDettagli(idCompany, idTavolo, idOrdine, nomeTavolo, coperti, piatti);
                AggiornaStatoTavolo(idCompany, idTavolo);

                string messageTxt = "Ordinazione inserita correttamente";
                return Ok(new { success = true, message = "Ordinazione inserita correttamente", orderId = idOrdine });

            }
            catch (SqlException ex)
            {
                string messageTxt = "ERRORE SHOP 003 : Errore nell'inserimento del prodotto nello shop";
                return InternalServerError(ex);

            }
            finally
            {
                SqlConnAcc.Close();
            }
        }

        [HttpGet]
        [Route("api/test/getComandaPerTavolo")]
        public IHttpActionResult GetComandaPerTavolo(Guid idTavolo)
        {
            string SQLstr = ConfigurationManager.ConnectionStrings["CloudConnection"].ConnectionString;
            SqlConnection conn = new SqlConnection(SQLstr);
            conn.Open();

            try
            {
                // Recupera l'ultima comanda associata al tavolo
                string comandaQuery = @"
            SELECT TOP 1
                IdCompany,
                IdTavolo,
                IdOrdine,
                DataOrdine,
                OraOrdine
            FROM resturant_orders
            WHERE IdTavolo = @IdTavolo AND IsDeleted = 0
            ORDER BY DataOrdine DESC";

                SqlCommand cmd = new SqlCommand(comandaQuery, conn);
                cmd.Parameters.AddWithValue("@IdTavolo", idTavolo);

                SqlDataReader reader = cmd.ExecuteReader();
                if (!reader.Read())
                {
                    return Ok(new { success = false, message = "Nessuna comanda trovata per questo tavolo." });
                }

                Guid idCompany = reader.GetGuid(0);
                Guid tavoloId = reader.GetGuid(1);
                Guid idOrdine = reader.GetGuid(2);
                reader.Close();

                // Recupera i dettagli dei piatti della comanda
                string dettagliQuery = @"
            SELECT
                idProdotto AS piattoId,
                Descrizione AS nome,
                Qta AS quantita,
                Turno AS turno,
                Prezzo AS prezzo
            FROM resturant_floor_orders
            WHERE IdOrdine = @IdOrdine AND IdTavolo = @IdTavolo AND IdCompany = @IdCompany AND IsDeleted = 0";

                SqlCommand cmdDettagli = new SqlCommand(dettagliQuery, conn);
                cmdDettagli.Parameters.AddWithValue("@IdOrdine", idOrdine);
                cmdDettagli.Parameters.AddWithValue("@IdTavolo", idTavolo);
                cmdDettagli.Parameters.AddWithValue("@IdCompany", idCompany);

                SqlDataReader readerDettagli = cmdDettagli.ExecuteReader();
                List<PiattoDto> piatti = new List<PiattoDto>();

                while (readerDettagli.Read())
                {
                    piatti.Add(new PiattoDto
                    {
                        piattoId = readerDettagli.GetGuid(0),
                        nome = readerDettagli.GetString(1),
                        quantita = readerDettagli.GetInt32(2),
                        turno = readerDettagli.GetString(3),
                        prezzo = readerDettagli.GetDecimal(4)
                    });
                }

                readerDettagli.Close();

                var result = new ComandaDto
                {
                    companyId = idCompany,
                    tavoloId = tavoloId,
                    cameriereId = 1, // Potresti recuperarlo se memorizzato
                    coperti = piatti.Sum(p => p.quantita), // O altro se salvato altrove
                    Piatti = piatti
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
            finally
            {
                conn.Close();
            }
        }

        // POST: api/test/getProdottiRaggruppatiPerFamiglia
        [HttpPost]
        [Route("api/test/sendToKitchen")]
        public IHttpActionResult SendToKitchen([FromBody] ComandaMovements request)
        {
            if (string.IsNullOrWhiteSpace(request.IdCompany) || string.IsNullOrWhiteSpace(request.IdTavolo))
                return Content(HttpStatusCode.BadRequest, new { success = false, message = "IdCompany o IdCategoria mancanti" });

            try
            {

                string SQLstr = ConfigurationManager.ConnectionStrings["CloudConnection"].ConnectionString;

                string strUpdateQuery = @"UPDATE resturant_floor_orders SET                                             
                                             Stato = @Stato                                             
                                        WHERE
                                             IdTavolo = @IdTavolo AND
                                             IdCompany = @IdCompany";

                SqlConnection SqlConnAcc = new SqlConnection(SQLstr);
                SqlConnAcc.Open();

                SqlCommand cmd2 = new SqlCommand();
                cmd2.Connection = SqlConnAcc;
                cmd2.CommandType = CommandType.Text;
                cmd2.CommandText = strUpdateQuery;


                cmd2.Parameters.Add("@IdCompany", SqlDbType.UniqueIdentifier).Value = new Guid(request.IdCompany);
                cmd2.Parameters.Add("@IdTavolo", SqlDbType.UniqueIdentifier).Value = new Guid(request.IdTavolo);
                cmd2.Parameters.Add("@Stato", SqlDbType.Int).Value = 1;

                cmd2.ExecuteNonQuery();
                cmd2.Parameters.Clear();

                string messageTxt = "Ordinazione inviata in cucina correttamente";

                return Ok(new
                {
                    success = true,
                    message = "Order successfully sent to kitchen",
                    tableId = request.IdTavolo
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Errore sendToKitchen: " + ex.Message);
                return InternalServerError(new Exception("Errore durante invio della comanda in cucina", ex));
            }
        }

        [HttpPost]
        [Route("api/test/closeTable")]
        public IHttpActionResult CloseTable([FromBody] ComandaMovements request)
        {
            if (string.IsNullOrWhiteSpace(request.IdCompany) || string.IsNullOrWhiteSpace(request.IdTavolo))
                return Content(HttpStatusCode.BadRequest, new { success = false, message = "IdCompany o IdCategoria mancanti" });

            try
            {
                DateTime dataChiusura = DateTime.Now;
                string oraChiusura = dataChiusura.ToString("HH:mm:ss");
                decimal importo = getImportoTavolo(request.IdCompany, request.IdCompany);

                string SQLstr = ConfigurationManager.ConnectionStrings["CloudConnection"].ConnectionString;

                string strUpdateQuery = @"UPDATE resturant_orders SET                                             
                                             Stato = @Stato                                             
                                        WHERE
                                             IdTavolo = @IdTavolo AND
                                             IdCompany = @IdCompany";

                SqlConnection SqlConnAcc = new SqlConnection(SQLstr);
                SqlConnAcc.Open();

                SqlCommand cmd2 = new SqlCommand();
                cmd2.Connection = SqlConnAcc;
                cmd2.CommandType = CommandType.Text;
                cmd2.CommandText = strUpdateQuery;


                cmd2.Parameters.Add("@IdCompany", SqlDbType.UniqueIdentifier).Value = new Guid(request.IdCompany);
                cmd2.Parameters.Add("@IdTavolo", SqlDbType.UniqueIdentifier).Value = new Guid(request.IdCompany);
                cmd2.Parameters.Add("@DataChiusura", SqlDbType.DateTime).Value = dataChiusura;
                cmd2.Parameters.Add("@OraChiusura", SqlDbType.UniqueIdentifier).Value = oraChiusura;
                cmd2.Parameters.Add("@Importo", SqlDbType.UniqueIdentifier).Value = importo;
                cmd2.Parameters.Add("@Stato", SqlDbType.Int).Value = 0;

                cmd2.ExecuteNonQuery();
                cmd2.Parameters.Clear();

                string messageTxt = "Ordinazione inviata in cucina correttamente";

                return Ok(new
                {
                    success = true,
                    message = "Order successfully sent to kitchen",
                    tableId = request.IdTavolo
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Errore sendToKitchen: " + ex.Message);
                return InternalServerError(new Exception("Errore durante invio della comanda in cucina", ex));
            }
        }

        private decimal getImportoTavolo(string idCompany, string idTavolo)
        {
            decimal importTavolo = 0;


            return importTavolo;
        }

        private bool IsProductPresent(string idTavolo, string idProdotto, string idCompany)
        {
            bool isPresentProd = false;

            string SQLstr = ConfigurationManager.ConnectionStrings["CloudConnection"].ConnectionString;
            SqlConnection conn = new SqlConnection(SQLstr);
            conn.Open();

            SqlCommand cmd3 = new SqlCommand();
            cmd3.Connection = conn;
            cmd3.CommandType = CommandType.Text;

            string sqlCheckIsPresent = "SELECT* FROM resturant_floor_orders WHERE IdTavolo = @IdTavolo AND idProdotto = @idProdotto AND IdCompany = @IdCompany";

            cmd3.CommandText = sqlCheckIsPresent;

            cmd3.Parameters.AddWithValue("@IdTavolo", new Guid(idTavolo));
            cmd3.Parameters.AddWithValue("@IdProdotto", new Guid(idProdotto));
            cmd3.Parameters.AddWithValue("@IdCompany", new Guid(idCompany));

            DataSet tab;
            SqlDataReader res = null;

            try
            {
                res = cmd3.ExecuteReader();
                tab = new DataSet();
                tab.Tables.Add("data");
                tab.Tables[0].Load(res);

                if (tab.Tables[0].Rows.Count > 0)
                {
                    isPresentProd = true;
                }

            }
            catch (SqlException ex)

            {
                string messageTxt = "Nessun tavolo selezionato.";

            }
            finally
            {
                conn.Close();
            }

            return isPresentProd;
        }

        private string TableOpenOrder(string idTavolo, string idCompany)
        {
            string orderId = "";

            string SQLstr = ConfigurationManager.ConnectionStrings["CloudConnection"].ConnectionString;
            SqlConnection conn = new SqlConnection(SQLstr);
            conn.Open();

            SqlCommand cmd3 = new SqlCommand();
            cmd3.Connection = conn;
            cmd3.CommandType = CommandType.Text;

            string sqlCheckIsPresent = "SELECT IdOrdine FROM resturant_orders WHERE IdTavolo = @IdTavolo AND IdCompany = @IdCompany AND Stato = @Stato";

            cmd3.CommandText = sqlCheckIsPresent;

            cmd3.Parameters.AddWithValue("@IdTavolo", new Guid(idTavolo));            
            cmd3.Parameters.AddWithValue("@IdCompany", new Guid(idCompany));
            cmd3.Parameters.AddWithValue("@Stato", 1);

            DataSet tab;
            SqlDataReader res = null;

            try
            {
                res = cmd3.ExecuteReader();
                tab = new DataSet();
                tab.Tables.Add("data");
                tab.Tables[0].Load(res);

                if (tab.Tables[0].Rows.Count > 0)
                {
                    orderId = tab.Tables[0].Rows[0]["IdOrdine"] == DBNull.Value ? "" : tab.Tables[0].Rows[0]["IdOrdine"].ToString().Trim();
                }

            }
            catch (SqlException ex)

            {
                string messageTxt = "Nessun tavolo aperto";
            }
            finally
            {
                conn.Close();
            }

            return orderId;
        }

        private bool AggiornaStatoTavolo(string idCompany, string idTavolo)
        {
            bool isUpdated = false;

            string SQLstr = ConfigurationManager.ConnectionStrings["CloudConnection"].ConnectionString;
            SqlConnection conn = new SqlConnection(SQLstr);
            conn.Open();

            SqlCommand cmd = new SqlCommand();
            cmd.Connection = conn;
            cmd.CommandType = CommandType.Text;

            string strUpdateQuery = @"UPDATE TavoliSala SET
                                             Stato = @Stato                                            
                                        WHERE
                                             IdTavolo = @IdTavolo AND
                                             IdCompany = @IdCompany";

            cmd.CommandText = strUpdateQuery;

            cmd.Parameters.AddWithValue("@IdTavolo", new Guid(idTavolo));
            cmd.Parameters.AddWithValue("@IdCompany", new Guid(idCompany));
            cmd.Parameters.AddWithValue("@Stato", 1);

            try
            {
                cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();

                isUpdated = true;
                return isUpdated;

            }
            catch (SqlException ex)
            {
                isUpdated = false;
                return isUpdated;

            }
            finally
            {
                conn.Close();
            }
        }

        private bool InserisciDettagli(string idCompany, string idTavolo, string idOrdine, string nomeTavolo, int coperti, List<PiattoDto> piatti)
        {
            bool isUpdated = false;


            try
            {
                foreach (var piatto in piatti)
                {
                    string piattoId = piatto.piattoId.ToString();
                    string nome = piatto.nome;
                    int quantita = piatto.quantita;
                    string turno = piatto.turno;
                    decimal prezzo = piatto.prezzo;

                    // Crea un oggetto anonimo
                    var piattoJsonObject = new
                    {
                        piattoId = piattoId,
                        nome = nome,
                        quantita = quantita,
                        turno = turno,
                        prezzo = prezzo
                    };

                    // Serializza l'oggetto in JSON
                    string jsonPiatto = JsonConvert.SerializeObject(piattoJsonObject);

                    Console.WriteLine(jsonPiatto);

                    // Passa il JSON del singolo piatto alla procedura
                    InserisciDettagliOrdine(idCompany, idTavolo, idOrdine, nomeTavolo, coperti, jsonPiatto);
                }

                isUpdated = true;
                return isUpdated;

            }
            catch (SqlException ex)
            {
                isUpdated = false;
                return isUpdated;

            }
            finally
            {

            }
        }

        private bool InserisciDettagliOrdine(string idCompany, string idTavolo, string idOrdine, string nomeTavolo, int coperti, string jsonPiatto)
        {
            bool isSuccess = false;

            DateTime dataOrdine = DateTime.Now;
            string oraOrdine = dataOrdine.ToString("HH:mm:ss");

            var piatto = JsonConvert.DeserializeObject<PiattoDto>(jsonPiatto);

            string piattoId = piatto.piattoId.ToString();
            string nome = piatto.nome;
            int quantita = piatto.quantita;
            string turno = piatto.turno;
            decimal prezzo = piatto.prezzo;

            string idCameriere = "00000000-0000-0000-0000-000000000000";

            string SQLstr = ConfigurationManager.ConnectionStrings["CloudConnection"].ConnectionString;
            SqlConnection conn = new SqlConnection(SQLstr);
            conn.Open();

            SqlCommand cmd = new SqlCommand();
            cmd.Connection = conn;
            cmd.CommandType = CommandType.Text;

            string strInsertQuery = @"INSERT INTO [dbo].[resturant_floor_orders]
									   ([IdCompany]
                                       ,[IdTavolo]
                                       ,[IdOrdine]
                                       ,[idProdotto]
                                       ,[idCameriere]
                                       ,[NomeTavolo]
                                       ,[Descrizione]
                                       ,[Qta]
                                       ,[Prezzo]
                                       ,[DataOrdine]
                                       ,[OraOrdine]
                                       ,[Stato]
                                       ,[Sequenza]
                                       ,[Turno]
                                       ,[Coperti]
                                       ,[isDeleted])
								 VALUES
									   (@IdCompany
                                       ,@IdTavolo
                                       ,@IdOrdine
                                       ,@idProdotto
                                       ,@idCameriere
                                       ,@NomeTavolo
                                       ,@Descrizione
                                       ,@Qta
                                       ,@Prezzo
                                       ,@DataOrdine
                                       ,@OraOrdine
                                       ,@Stato
                                       ,@Sequenza
                                       ,@Turno
                                       ,@Coperti
                                       ,@isDeleted)";

            cmd.CommandText = strInsertQuery;

            cmd.Parameters.Add("@IdCompany", SqlDbType.UniqueIdentifier).Value = new Guid(idCompany);
            cmd.Parameters.Add("@IdTavolo", SqlDbType.UniqueIdentifier).Value = new Guid(idTavolo);
            cmd.Parameters.Add("@IdOrdine", SqlDbType.UniqueIdentifier).Value = new Guid(idOrdine);
            cmd.Parameters.Add("@IdProdotto", SqlDbType.UniqueIdentifier).Value = new Guid(piattoId);
            cmd.Parameters.Add("@IdCameriere", SqlDbType.UniqueIdentifier).Value = new Guid(idCameriere);
            cmd.Parameters.Add("@NomeTavolo", SqlDbType.VarChar).Value = nomeTavolo;
            cmd.Parameters.Add("@Descrizione", SqlDbType.VarChar).Value = nome;
            cmd.Parameters.Add("@Qta", SqlDbType.Int).Value = quantita;
            cmd.Parameters.Add("@Prezzo", SqlDbType.Decimal).Value = prezzo;
            cmd.Parameters.Add("@DataOrdine", SqlDbType.DateTime).Value = dataOrdine;
            cmd.Parameters.Add("@OraOrdine", SqlDbType.Time).Value = oraOrdine;
            cmd.Parameters.Add("@Stato", SqlDbType.Int).Value = 0;
            cmd.Parameters.Add("@Sequenza", SqlDbType.Int).Value = 0;
            cmd.Parameters.Add("@Turno", SqlDbType.VarChar).Value = turno;
            cmd.Parameters.Add("@Coperti", SqlDbType.VarChar).Value = coperti;
            cmd.Parameters.Add("@IsDeleted", SqlDbType.Bit).Value = false;

            try
            {
                cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();
                isSuccess = true;
                return isSuccess;
            }
            catch (SqlException ex)
            {
                isSuccess = false;
                return isSuccess;
            }
            finally
            {
                conn.Close();
            }
        }
    }
}


