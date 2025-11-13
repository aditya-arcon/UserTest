// UserTest/Seed/DepartmentSeedingTests.cs
using IDV_Backend.Data;
using IDV_Backend.Models.Departments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace UserTest.Seed
{
    [TestFixture]
    public class DepartmentSeedingTests
    {
        private static ServiceProvider BuildProvider(string dbName)
        {
            var sc = new ServiceCollection();
            sc.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
            return sc.BuildServiceProvider();
        }

        [Test]
        public async Task Seeds_All_Departments_When_Empty()
        {
            var sp = BuildProvider(Guid.NewGuid().ToString("N"));
            await sp.EnsureDepartmentsAsync();

            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var names = await db.Departments.AsNoTracking().OrderBy(d => d.DeptId).Select(d => d.DeptName).ToListAsync();

            string[] expected =
            {
                "Human Resources",
                "Finance",
                "Marketing",
                "Sales",
                "Customer Support",
                "IT & Security",
                "Legal",
                "Operations"
            };

            Assert.That(names.Count, Is.EqualTo(expected.Length), "Unexpected department count after seed.");
            CollectionAssert.AreEquivalent(expected, names, "Department names mismatch after seed.");
        }

        [Test]
        public async Task Seeding_Is_Idempotent()
        {
            var sp = BuildProvider(Guid.NewGuid().ToString("N"));

            // Run twice
            await sp.EnsureDepartmentsAsync();
            await sp.EnsureDepartmentsAsync();

            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var all = await db.Departments.AsNoTracking().ToListAsync();
            Assert.That(all.Count, Is.EqualTo(8), "Seeding should not create duplicates.");

            // No duplicate names ignoring case/spacing
            var distinct = all.Select(d => d.DeptName.Trim().ToLowerInvariant()).Distinct().Count();
            Assert.That(distinct, Is.EqualTo(8), "Names should be distinct.");
        }

        [Test]
        public async Task Does_Not_Add_If_Already_Exists_CaseInsensitive()
        {
            var sp = BuildProvider(Guid.NewGuid().ToString("N"));
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Departments.Add(new Department { DeptName = "finance" }); // lower-case existing
                await db.SaveChangesAsync();
            }

            await sp.EnsureDepartmentsAsync();

            using var scope2 = sp.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var count = await db2.Departments.CountAsync();
            Assert.That(count, Is.EqualTo(8), "Seeder should fill to total 8, accounting for pre-existing case-insensitive name.");
        }
    }
}
