using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Kroeg.Server.BackgroundTasks;
using Kroeg.Server.Services;
using Microsoft.EntityFrameworkCore.Design;

namespace Kroeg.Server.Models
{
    public class APContext : IdentityDbContext<APUser>
    {
        public class ContextBuilder : IDesignTimeDbContextFactory<APContext>
        {
            APContext IDesignTimeDbContextFactory<APContext>.CreateDbContext(string[] args)
            {
                return new APContext(new DbContextOptionsBuilder().UseNpgsql("Host=localhost;Username=postgres;Password=postgres;Database=kroeg2").Options, null);
            }
        }

        private readonly INotifier _notifier;

        public APContext(DbContextOptions options, INotifier notifier)
            : base(options)
        {
            _notifier = notifier;
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<JWKEntry>()
                .HasKey(a => new { a.Id, a.OwnerId });
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            var newEventQueue = ChangeTracker.HasChanges() && ChangeTracker.Entries<EventQueueItem>().Any(a => a.State == EntityState.Added);
            var collectionItemEntries = ChangeTracker.Entries<CollectionItem>().Where(a => a.State == EntityState.Added).Select(a => a.Entity).ToList();

            var returnValue = await base.SaveChangesAsync(cancellationToken);

            if (newEventQueue)
            {
                await _notifier.Notify(BackgroundTaskQueuer.BackgroundTaskPath, "new");
            }

            foreach (var collectionItemEntry in collectionItemEntries)
            {
                await _notifier.Notify("collection/" + collectionItemEntry.CollectionId, collectionItemEntry.ElementId);
            }

            return returnValue;
        }

        public async Task<SalmonKey> GetKey(string entityId)
        {
            var res = await SalmonKeys.FirstOrDefaultAsync(a => a.EntityId == entityId);
            if (res != null) return res;

            res = new SalmonKey()
            {
                EntityId = entityId,
                PrivateKey = Salmon.MagicKey.Generate().PrivateKey
            };

            SalmonKeys.Add(res);
            await SaveChangesAsync();

            return res;
        }

        public DbSet<APDBEntity> Entities { get; set; }
        public DbSet<CollectionItem> CollectionItems { get; set; }
        public DbSet<UserActorPermission> UserActorPermissions { get; set; }
        public DbSet<EventQueueItem> EventQueue { get; set; }

        public DbSet<SalmonKey> SalmonKeys { get; set; }
        public DbSet<WebsubSubscription> WebsubSubscriptions { get; set; }
        public DbSet<WebSubClient> WebSubClients { get; set; }
        public DbSet<JWKEntry> JsonWebKeys { get; set; }
    }
}
