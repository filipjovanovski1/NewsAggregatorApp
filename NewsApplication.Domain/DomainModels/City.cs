using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.DomainModels
{
    public class City : BaseEntity
    {
        [Required]
        public string Name { get; set; } = null!;
        [Required]
        public string CountryName { get; set; } = null!;
        [Required]
        [StringLength(2)]
        public string CountryIso2 { get; set; } = null!;
        [ForeignKey(nameof(CountryIso2))] public Country? Country { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }

    }
}
