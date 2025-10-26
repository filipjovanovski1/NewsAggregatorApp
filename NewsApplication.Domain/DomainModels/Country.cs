using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.DomainModels
{
    
    public class Country 
    {
        [Key]
        [StringLength(2)]
        public string Iso2 { get; set; } = null!;  // "MK", "DE", etc.
        [StringLength(3)]
        
        public string? Iso3 { get; set; }
        [Required]
        public string Name { get; set; } = null!;

        // Optional, persisted once we compute them
        public double? CentroidLat { get; set; }
        public double? CentroidLng { get; set; }
    }

}
