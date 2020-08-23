﻿using Microsoft.EntityFrameworkCore;
using System;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Newtonsoft.Json;

namespace phase_2_back_end.Models
{
    public class ApplicationDatabase : DbContext
    {
        public ApplicationDatabase(DbContextOptions<ApplicationDatabase> options) : base(options)
        {
        }

        public DbSet<Canvas> Canvas { get; set; }
        public DbSet<ColorData> ColorData { get; set; }
        public DbSet<HistoricalData> HistoricalData { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
           .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
           .AddJsonFile("appsettings.json")
           .Build();
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Canvas>()
                .Property(p => p.CanvasID)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<ColorData>()
                .Property(p => p.ColorDataID)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<HistoricalData>()
                .Property(p => p.Id)
                .ValueGeneratedOnAdd();
        }

		public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default(CancellationToken))
		{
			var historicalDataEntries = OnBeforeSaveChanges();
			var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
			await OnAfterSaveChanges(historicalDataEntries);
			return result;
		}

		private List<HistoricalDataEntry> OnBeforeSaveChanges()
        {
            ChangeTracker.DetectChanges();
            var historicalDataEntries = new List<HistoricalDataEntry>();
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.Entity is HistoricalData || entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                    continue;

                var historicalDataEntry = new HistoricalDataEntry(entry);
                //historicalDataEntry.TableName = entry.Metadata.Relational().TableName;
                historicalDataEntry.TableName = entry.Metadata.GetTableName();
                historicalDataEntries.Add(historicalDataEntry);

                foreach (var property in entry.Properties)
                {
                    if (property.IsTemporary)
                    {
                        // value will be generated by the database, get the value after saving
                        historicalDataEntry.TemporaryProperties.Add(property);
                        continue;
                    }

                    string propertyName = property.Metadata.Name;
                    if (property.Metadata.IsPrimaryKey())
                    {
                        historicalDataEntry.KeyValues[propertyName] = property.CurrentValue;
                        continue;
                    }

                    switch (entry.State)
                    {
                        case EntityState.Added:
                            historicalDataEntry.NewValues[propertyName] = property.CurrentValue;
                            break;

                        case EntityState.Deleted:
                            historicalDataEntry.OldValues[propertyName] = property.OriginalValue;
                            break;

                        case EntityState.Modified:
                            if (property.IsModified)
                            {
                                historicalDataEntry.OldValues[propertyName] = property.OriginalValue;
                                historicalDataEntry.NewValues[propertyName] = property.CurrentValue;
                            }
                            break;
                    }
                }
            }

            // Save audit entities that have all the modifications
            foreach (var historicalDataEntry in historicalDataEntries.Where(_ => !_.HasTemporaryProperties))
            {
                HistoricalData.Add(historicalDataEntry.ToHistoricalData());
            }

            // keep a list of entries where the value of some properties are unknown at this step
            return historicalDataEntries.Where(_ => _.HasTemporaryProperties).ToList();
        }

        private Task OnAfterSaveChanges(List<HistoricalDataEntry> auditEntries)
        {
            if (auditEntries == null || auditEntries.Count == 0)
            {
                return Task.CompletedTask;
            }

            foreach (var auditEntry in auditEntries)
            {
                // Get the final value of the temporary properties
                foreach (var prop in auditEntry.TemporaryProperties)
                {
                    if (prop.Metadata.IsPrimaryKey())
                    {
                        auditEntry.KeyValues[prop.Metadata.Name] = prop.CurrentValue;
                    }
                    else
                    {
                        auditEntry.NewValues[prop.Metadata.Name] = prop.CurrentValue;
                    }
                }

                // Save the Audit entry
                HistoricalData.Add(auditEntry.ToHistoricalData());
            }
            return SaveChangesAsync();
        }
    }


    public class HistoricalDataEntry
    {
        public HistoricalDataEntry(EntityEntry entry)
        {
            Entry = entry;
        }

        public EntityEntry Entry { get; }
        public string TableName { get; set; }
        public Dictionary<string, object> KeyValues { get; } = new Dictionary<string, object>();
        public Dictionary<string, object> OldValues { get; } = new Dictionary<string, object>();
        public Dictionary<string, object> NewValues { get; } = new Dictionary<string, object>();
        public List<PropertyEntry> TemporaryProperties { get; } = new List<PropertyEntry>();

        public bool HasTemporaryProperties => TemporaryProperties.Any();

        public HistoricalData ToHistoricalData()
        {
            var historicalData = new HistoricalData();
            historicalData.TableName = TableName;
            historicalData.DateTime = DateTime.UtcNow;
            historicalData.KeyValues = JsonConvert.SerializeObject(KeyValues);
            historicalData.OldValues = OldValues.Count == 0 ? null : JsonConvert.SerializeObject(OldValues);
            historicalData.NewValues = NewValues.Count == 0 ? null : JsonConvert.SerializeObject(NewValues);
            return historicalData;
        }
    }
}
