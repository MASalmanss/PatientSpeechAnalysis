using Microsoft.EntityFrameworkCore;
using PatientSpeechAnalysis.Models;

namespace PatientSpeechAnalysis.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<PatientAnalysis> PatientAnalyses => Set<PatientAnalysis>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
}
