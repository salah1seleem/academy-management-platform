using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;
public static class PlayerLevelHistory
{
    public static string Hash(Guid enrollment,PlacementLevelWrite b)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(enrollment+JsonSerializer.Serialize(b))));
    public static async Task<object> Read(FoundationDbContext db,Guid academy,Guid enrollment,bool privateNotes=false)
    {
        var rows=await db.PlayerLevelReviews.AsNoTracking().Where(r=>r.AcademyId==academy&&r.SportEnrollmentId==enrollment).OrderByDescending(r=>r.Revision).Take(21).ToArrayAsync();
        var zone=TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a=>a.Id==academy).Select(a=>a.TimeZone).SingleAsync());
        return new{items=rows.Take(20).Select(r=>new{r.Id,r.Revision,r.Action,r.FromLevelName,r.ToLevelName,r.ActorName,r.CreatedAtUtc,reviewedOn=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.CreatedAtUtc,zone).DateTime),note=privateNotes?r.PrivateNote:null}),hasMore=rows.Length>20};
    }
}
