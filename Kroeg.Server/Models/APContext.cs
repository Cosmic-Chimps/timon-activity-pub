using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Kroeg.Server.BackgroundTasks;
using Kroeg.Server.Services;
using Microsoft.EntityFrameworkCore.Design;
using System;

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

            builder.Entity<TripleAttribute>()
                .HasIndex(a => a.Uri);
            
            builder.Entity<Triple>()
                .HasIndex(a => a.SubjectEntityId);
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

            var neededIds = collectionItemEntries.Select(a => a.ElementId).ToList();
            var neededEntities = await TripleEntities.Where(a => neededIds.Contains(a.EntityId)).Select(a => a.Id.Uri).ToListAsync();
            foreach (var collectionItemEntry in collectionItemEntries.Zip(neededEntities, (a, b) => new System.Tuple<CollectionItem, string>(a, b)))
            {
                await _notifier.Notify("collection/" + collectionItemEntry.Item1.CollectionId, collectionItemEntry.Item2);
            }

            return returnValue;
        }

        public bool Dirty { get; set; }

        public async Task<SalmonKey> GetKey(string entityId)
        {
            var inverse = await Attributes.FirstOrDefaultAsync(a => a.Uri == entityId);
            if (inverse == null) return null;

            var entity = await TripleEntities.FirstOrDefaultAsync(a => a.IdId == inverse.AttributeId);
            if (entity == null) return null;

            var res = await SalmonKeys.FirstOrDefaultAsync(a => a.EntityId == entity.EntityId);
            if (res != null) return res;

            res = new SalmonKey()
            {
                EntityId = entity.EntityId,
                PrivateKey = Salmon.MagicKey.Generate().PrivateKey
            };

            SalmonKeys.Add(res);
            await SaveChangesAsync();

            return res;
        }

        public DbSet<CollectionItem> CollectionItems { get; set; }
        public DbSet<UserActorPermission> UserActorPermissions { get; set; }
        public DbSet<EventQueueItem> EventQueue { get; set; }

        public DbSet<SalmonKey> SalmonKeys { get; set; }
        public DbSet<WebsubSubscription> WebsubSubscriptions { get; set; }
        public DbSet<WebSubClient> WebSubClients { get; set; }
        public DbSet<JWKEntry> JsonWebKeys { get; set; }

        public DbSet<TripleAttribute> Attributes { get; set; }
        public DbSet<Triple> Triples { get; set; }
        public DbSet<APTripleEntity> TripleEntities { get; set; }
    }
}
