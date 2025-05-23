using System.Collections.Generic;
using System;

namespace VendoloApi.Models
{
    public class ComandaDto
    {
        public Guid companyId { get; set; }
        public Guid tavoloId { get; set; }
        public int cameriereId { get; set; }
        public int coperti { get; set; }
        public List<PiattoDto> Piatti { get; set; }
    }

    public class PiattoDto
    {
        public Guid piattoId { get; set; }
        public string nome { get; set; }
        public int quantita { get; set; }
        public string turno { get; set; }
    }
}