using System.ComponentModel.DataAnnotations;

namespace MatchForecast.Models.Request;

// Google Identity Services butonunun döndürdüğü ID token ("credential"). "idToken" alanı log'da maskelenir.
public sealed record GoogleLoginRequest([Required, MaxLength(4096)] string IdToken);