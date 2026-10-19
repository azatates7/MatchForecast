using System.ComponentModel.DataAnnotations.Schema;

namespace MatchForecast.Data.Entity.Common
{
    public abstract class BaseEntity
    {
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = "System";

        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }

        public int Status { get; set; }

        //[Column(TypeName = "NUMBER(1)")] // Oracle'da boolean yok, NUMBER(1) kullanılır. 0=false, 1=true
        public bool IsDeleted { get; set; }
    }
}