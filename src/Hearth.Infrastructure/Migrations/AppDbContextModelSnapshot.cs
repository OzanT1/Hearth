using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
namespace Hearth.Infrastructure.Migrations;
[DbContext(typeof(AppDbContext))]
public sealed class AppDbContextModelSnapshot : Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => InitialModel.Build(modelBuilder);
}
