using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace cliqx.auth.api.Models;

public class UserForgetCode
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string ForgetCode { get; set; }
    public DateTime ExpirationDate { get; set; } = DateTime.Now.AddMinutes(30);
}
