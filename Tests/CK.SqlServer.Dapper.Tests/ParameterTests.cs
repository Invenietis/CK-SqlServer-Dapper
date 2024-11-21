using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Dynamic;
using System.Linq;
using Xunit;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Dapper;
using static CK.Testing.SqlServerTestHelper;

namespace CK.SqlServer.Dapper.Tests;

public class ParameterTests : TestBase
{
    public class DbParams : SqlMapper.IDynamicParameters, IEnumerable<IDbDataParameter>
    {
        private readonly List<IDbDataParameter> parameters = new List<IDbDataParameter>();
        public IEnumerator<IDbDataParameter> GetEnumerator() { return parameters.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        public void Add( IDbDataParameter value )
        {
            parameters.Add( value );
        }

        void SqlMapper.IDynamicParameters.AddParameters( IDbCommand command, SqlMapper.Identity identity )
        {
            foreach( IDbDataParameter parameter in parameters )
                command.Parameters.Add( parameter );
        }
    }

    private static List<Microsoft.Data.SqlClient.Server.SqlDataRecord> CreateSqlDataRecordList( IEnumerable<int> numbers )
    {
        var number_list = new List<Microsoft.Data.SqlClient.Server.SqlDataRecord>();

        // Create an SqlMetaData object that describes our table type.
        Microsoft.Data.SqlClient.Server.SqlMetaData[] tvp_definition = { new Microsoft.Data.SqlClient.Server.SqlMetaData( "n", SqlDbType.Int ) };

        foreach( int n in numbers )
        {
            // Create a new record, using the metadata array above.
            var rec = new Microsoft.Data.SqlClient.Server.SqlDataRecord( tvp_definition );
            rec.SetInt32( 0, n );    // Set the value.
            number_list.Add( rec );      // Add it to the list.
        }

        return number_list;
    }

    private class IntDynamicParam : SqlMapper.IDynamicParameters
    {
        private readonly IEnumerable<int> numbers;
        public IntDynamicParam( IEnumerable<int> numbers )
        {
            this.numbers = numbers;
        }

        public void AddParameters( IDbCommand command, SqlMapper.Identity identity )
        {
            var sqlCommand = (SqlCommand)command;
            sqlCommand.CommandType = CommandType.StoredProcedure;

            var number_list = CreateSqlDataRecordList( numbers );

            // Add the table parameter.
            var p = sqlCommand.Parameters.Add( "ints", SqlDbType.Structured );
            p.Direction = ParameterDirection.Input;
            p.TypeName = "int_list_type";
            p.Value = number_list;
        }
    }

    private class IntCustomParam : SqlMapper.ICustomQueryParameter
    {
        private readonly IEnumerable<int> numbers;
        public IntCustomParam( IEnumerable<int> numbers )
        {
            this.numbers = numbers;
        }

        public void AddParameter( IDbCommand command, string name )
        {
            var sqlCommand = (SqlCommand)command;
            sqlCommand.CommandType = CommandType.StoredProcedure;

            var number_list = CreateSqlDataRecordList( numbers );

            // Add the table parameter.
            var p = sqlCommand.Parameters.Add( name, SqlDbType.Structured );
            p.Direction = ParameterDirection.Input;
            p.TypeName = "int_list_type";
            p.Value = number_list;
        }
    }

    /* TODO:
     * 
    public void TestMagicParam()
    {
        // magic params allow you to pass in single params without using an anon class
        // this test fails for now, but I would like to support a single param by parsing the sql with regex and remapping. 

        var first = controller.Query("select @a as a", 1).First();
        Assert.Equal(first.a, 1);
    }
     * */

    [Fact]
    public void TestDoubleParam()
    {
        Assert.Equal( 0.1d, controller.Query<double>( "select @d", new { d = 0.1d } ).First() );
    }

    [Fact]
    public void TestBoolParam()
    {
        Assert.False( controller.Query<bool>( "select @b", new { b = false } ).First() );
    }

    // http://code.google.com/p/dapper-dot-net/issues/detail?id=70
    // https://connect.microsoft.com/VisualStudio/feedback/details/381934/sqlparameter-dbtype-dbtype-time-sets-the-parameter-to-sqldbtype-datetime-instead-of-sqldbtype-time

    [Fact]
    public void TestTimeSpanParam()
    {
        Assert.Equal( controller.Query<TimeSpan>( "select @ts", new { ts = TimeSpan.FromMinutes( 42 ) } ).First(), TimeSpan.FromMinutes( 42 ) );
    }

    [Fact]
    public void PassInIntArray()
    {
        Assert.Equal(
            new[] { 1, 2, 3 },
            controller.Query<int>( "select * from (select 1 as Id union all select 2 union all select 3) as X where Id in @Ids", new { Ids = new int[] { 1, 2, 3 }.AsEnumerable() } )
        );
    }

    [Fact]
    public void PassInEmptyIntArray()
    {
        Assert.Equal(
            new int[0],
            controller.Query<int>( "select * from (select 1 as Id union all select 2 union all select 3) as X where Id in @Ids", new { Ids = new int[0] } )
        );
    }

    [Fact]
    public void TestExecuteCommandWithHybridParameters()
    {
        var p = new DynamicParameters( new { a = 1, b = 2 } );
        p.Add( "c", dbType: DbType.Int32, direction: ParameterDirection.Output );
        controller.Execute( "set @c = @a + @b", p );
        Assert.Equal( 3, p.Get<int>( "@c" ) );
    }

    [Fact]
    public void GuidIn_SO_24177902()
    {
        // invent and populate
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid();
        controller.Execute( "create table #foo (i int, g uniqueidentifier)" );
        controller.Execute( "insert #foo(i,g) values(@i,@g)",
            new[] { new { i = 1, g = a }, new { i = 2, g = b },
            new { i = 3, g = c },new { i = 4, g = d }} );

        // check that rows 2&3 yield guids b&c
        var guids = controller.Query<Guid>( "select g from #foo where i in (2,3)" ).ToArray();
        Assert.Equal( 2, guids.Length );
        Assert.DoesNotContain( a, guids );
        Assert.Contains( b, guids );
        Assert.Contains( c, guids );
        Assert.DoesNotContain( d, guids );

        // in query on the guids
        var rows = controller.Query( "select * from #foo where g in @guids order by i", new { guids } )
            .Select( row => new { i = (int)row.i, g = (Guid)row.g } ).ToArray();
        Assert.Equal( 2, rows.Length );
        Assert.Equal( 2, rows[0].i );
        Assert.Equal( b, rows[0].g );
        Assert.Equal( 3, rows[1].i );
        Assert.Equal( c, rows[1].g );
    }

    [FactUnlessCaseSensitiveDatabase]
    public void TestParameterInclusionNotSensitiveToCurrentCulture()
    {
        // note this might fail if your database server is case-sensitive
        CultureInfo current = ActiveCulture;
        try
        {
            ActiveCulture = new CultureInfo( "tr-TR" );

            controller.Query<int>( "select @pid", new { PId = 1 } ).Single();
        }
        finally
        {
            ActiveCulture = current;
        }
    }

    [Fact]
    public void TestMassiveStrings()
    {
        var str = new string( 'X', 20000 );
        Assert.Equal( controller.Query<string>( "select @a", new { a = str } ).First(), str );
    }

    [Fact]
    public void TestTVPWithAnonymousObject()
    {
        try
        {
            controller.Execute( "CREATE TYPE int_list_type AS TABLE (n int NOT NULL PRIMARY KEY)" );
            controller.Execute( "CREATE PROC get_ints @integers int_list_type READONLY AS select * from @integers" );

            var nums = controller.Query<int>( "get_ints", new { integers = new IntCustomParam( new int[] { 1, 2, 3 } ) }, commandType: CommandType.StoredProcedure ).ToList();
            Assert.Equal( 1, nums[0] );
            Assert.Equal( 2, nums[1] );
            Assert.Equal( 3, nums[2] );
            Assert.Equal( 3, nums.Count );
        }
        finally
        {
            try
            {
                controller.Execute( "DROP PROC get_ints" );
            }
            finally
            {
                controller.Execute( "DROP TYPE int_list_type" );
            }
        }
    }

    [Fact]
    public void TestTVPWithAnonymousEmptyObject()
    {
        try
        {
            controller.Execute( "CREATE TYPE int_list_type AS TABLE (n int NOT NULL PRIMARY KEY)" );
            controller.Execute( "CREATE PROC get_ints @integers int_list_type READONLY AS select * from @integers" );

            var nums = controller.Query<int>( "get_ints", new { integers = new IntCustomParam( new int[] { } ) }, commandType: CommandType.StoredProcedure ).ToList();
            Assert.Equal( 1, nums[0] );
            Assert.Equal( 2, nums[1] );
            Assert.Equal( 3, nums[2] );
            Assert.Equal( 3, nums.Count );
        }
        catch( ArgumentException ex )
        {
            Assert.True( string.Compare( ex.Message, "There are no records in the SqlDataRecord enumeration. To send a table-valued parameter with no rows, use a null reference for the value instead." ) == 0 );
        }
        finally
        {
            try
            {
                controller.Execute( "DROP PROC get_ints" );
            }
            finally
            {
                controller.Execute( "DROP TYPE int_list_type" );
            }
        }
    }

    // SQL Server specific test to demonstrate TVP 
    [Fact]
    public void TestTVP()
    {
        try
        {
            controller.Execute( "CREATE TYPE int_list_type AS TABLE (n int NOT NULL PRIMARY KEY)" );
            controller.Execute( "CREATE PROC get_ints @ints int_list_type READONLY AS select * from @ints" );

            var nums = controller.Query<int>( "get_ints", new IntDynamicParam( new int[] { 1, 2, 3 } ) ).ToList();
            Assert.Equal( 1, nums[0] );
            Assert.Equal( 2, nums[1] );
            Assert.Equal( 3, nums[2] );
            Assert.Equal( 3, nums.Count );
        }
        finally
        {
            try
            {
                controller.Execute( "DROP PROC get_ints" );
            }
            finally
            {
                controller.Execute( "DROP TYPE int_list_type" );
            }
        }
    }

    private class DynamicParameterWithIntTVP : DynamicParameters, SqlMapper.IDynamicParameters
    {
        private readonly IEnumerable<int> numbers;
        public DynamicParameterWithIntTVP( IEnumerable<int> numbers )
        {
            this.numbers = numbers;
        }

        public new void AddParameters( IDbCommand command, SqlMapper.Identity identity )
        {
            base.AddParameters( command, identity );

            var sqlCommand = (SqlCommand)command;
            sqlCommand.CommandType = CommandType.StoredProcedure;

            var number_list = CreateSqlDataRecordList( numbers );

            // Add the table parameter.
            var p = sqlCommand.Parameters.Add( "ints", SqlDbType.Structured );
            p.Direction = ParameterDirection.Input;
            p.TypeName = "int_list_type";
            p.Value = number_list;
        }
    }

    [Fact]
    public void TestTVPWithAdditionalParams()
    {
        try
        {
            controller.Execute( "CREATE TYPE int_list_type AS TABLE (n int NOT NULL PRIMARY KEY)" );
            controller.Execute( "CREATE PROC get_values @ints int_list_type READONLY, @stringParam varchar(20), @dateParam datetime AS select i.*, @stringParam as stringParam, @dateParam as dateParam from @ints i" );

            var dynamicParameters = new DynamicParameterWithIntTVP( new int[] { 1, 2, 3 } );
            dynamicParameters.AddDynamicParams( new { stringParam = "stringParam", dateParam = new DateTime( 2012, 1, 1 ) } );

            var results = controller.Query( "get_values", dynamicParameters, commandType: CommandType.StoredProcedure ).ToList();
            Assert.Equal( 3, results.Count );
            for( int i = 0; i < results.Count; i++ )
            {
                var result = results[i];
                Assert.Equal( i + 1, result.n );
                Assert.Equal( "stringParam", result.stringParam );
                Assert.Equal( new DateTime( 2012, 1, 1 ), result.dateParam );
            }
        }
        finally
        {
            try
            {
                controller.Execute( "DROP PROC get_values" );
            }
            finally
            {
                controller.Execute( "DROP TYPE int_list_type" );
            }
        }
    }

    [Fact]
    public void TestSqlDataRecordListParametersWithAsTableValuedParameter()
    {
        try
        {
            controller.Execute( "CREATE TYPE int_list_type AS TABLE (n int NOT NULL PRIMARY KEY)" );
            controller.Execute( "CREATE PROC get_ints @integers int_list_type READONLY AS select * from @integers" );

            var records = CreateSqlDataRecordList( new int[] { 1, 2, 3 } );

            var nums = controller.Query<int>( "get_ints", new { integers = records.AsTableValuedParameter() }, commandType: CommandType.StoredProcedure ).ToList();
            Assert.Equal( new int[] { 1, 2, 3 }, nums );

            nums = controller.Query<int>( "select * from @integers", new { integers = records.AsTableValuedParameter( "int_list_type" ) } ).ToList();
            Assert.Equal( new int[] { 1, 2, 3 }, nums );

            try
            {
                controller.Query<int>( "select * from @integers", new { integers = records.AsTableValuedParameter() } ).First();
                throw new InvalidOperationException();
            }
            catch( Exception ex )
            {
                ex.Message.Equals( "The table type parameter 'ids' must have a valid type name." );
            }
        }
        finally
        {
            try
            {
                controller.Execute( "DROP PROC get_ints" );
            }
            finally
            {
                controller.Execute( "DROP TYPE int_list_type" );
            }
        }
    }

    // This doesn't work as expected.
    //[Fact]
    //public void TestEmptySqlDataRecordListParametersWithAsTableValuedParameter()
    //{
    //    try { controller.Execute( "DROP TYPE TEST.int_list_type;" ); } catch { }
    //    try { controller.Execute( "DROP PROC TEST.get_ints;" ); } catch { }
    //    try { controller.Execute( "create schema TEST;" ); } catch { }
    //    controller.Execute( "CREATE TYPE TEST.int_list_type AS TABLE (n int NOT NULL PRIMARY KEY);" );
    //    controller.Execute( "CREATE PROC TEST.get_ints @integers TEST.int_list_type READONLY AS select * from @integers;" );

    //    var emptyRecord = CreateSqlDataRecordList( Enumerable.Empty<int>() );

    //    // Message: System.ArgumentException :
    //    // There are no records in the SqlDataRecord enumeration.
    //    // To send a table-valued parameter with no rows, use a null reference for the value instead.
    //    //
    //    var nums = controller.Query<int>( "TEST.get_ints", new { integers = emptyRecord.AsTableValuedParameter() }, commandType: CommandType.StoredProcedure ).ToList();

    //    // Message: System.Data.SqlClient.SqlException : Operand type clash: nvarchar is incompatible with int_list_type
    //    // emptyRecord = null;
    //    // var nums = controller.Query<int>( "TEST.get_ints", new { integers = emptyRecord }, commandType: CommandType.StoredProcedure ).ToList();
    //    Assert.True( nums.Count == 0 );
    //}

    [Fact]
    public void TestSqlDataRecordListParametersWithTypeHandlers()
    {
        try
        {
            controller.Execute( "CREATE TYPE int_list_type AS TABLE (n int NOT NULL PRIMARY KEY)" );
            controller.Execute( "CREATE PROC get_ints @integers int_list_type READONLY AS select * from @integers" );

            // Variable type has to be IEnumerable<SqlDataRecord> for TypeHandler to kick in.
            IEnumerable<Microsoft.Data.SqlClient.Server.SqlDataRecord> records = CreateSqlDataRecordList( new int[] { 1, 2, 3 } );

            var nums = controller.Query<int>( "get_ints", new { integers = records }, commandType: CommandType.StoredProcedure ).ToList();
            Assert.Equal( new int[] { 1, 2, 3 }, nums );

            try
            {
                controller.Query<int>( "select * from @integers", new { integers = records } ).First();
                throw new InvalidOperationException();
            }
            catch( Exception ex )
            {
                ex.Message.Equals( "The table type parameter 'ids' must have a valid type name." );
            }
        }
        finally
        {
            try
            {
                controller.Execute( "DROP PROC get_ints" );
            }
            finally
            {
                controller.Execute( "DROP TYPE int_list_type" );
            }
        }
    }

#if !NETCOREAPP1_0
    [Fact]
    public void DataTableParameters()
    {
        try { controller.Execute( "drop proc TEST.DataTableParameters;" ); } catch { /* don't care */ }
        try { controller.Execute( "drop table TEST.DataTableParameters;" ); } catch { /* don't care */ }
        try { controller.Execute( "drop type TEST.MyTVPType;" ); } catch { /* don't care */ }
        try { controller.Execute( "create schema TEST;" ); } catch { /* don't care */ }

        controller.Execute( "create type TEST.MyTVPType as table (id int)" );
        controller.Execute( "create proc TEST.DataTableParameters @ids TEST.MyTVPType readonly as select count(1) from @ids" );
        Assert.Equal( ConnectionState.Open, controller.Connection.State );

        var table = new DataTable { Columns = { { "id", typeof( int ) } }, Rows = { { 1 }, { 2 }, { 3 } } };

        int count = controller.Query<int>( "TEST.DataTableParameters", new { ids = table.AsTableValuedParameter() }, commandType: CommandType.StoredProcedure ).First();
        Assert.Equal( 3, count );

        count = controller.Query<int>( "select count(1) from @ids", new { ids = table.AsTableValuedParameter( "TEST.MyTVPType" ) } ).First();
        Assert.Equal( 3, count );

        try
        {
            controller.Query<int>( "select count(1) from @ids", new { ids = table.AsTableValuedParameter() } ).First();
            throw new InvalidOperationException();
        }
        catch( Exception ex )
        {
            ex.Message.Equals( "The table type parameter 'ids' must have a valid type name." );
        }
    }

    [Fact]
    public void SO29533765_DataTableParametersViaDynamicParameters()
    {
        try { controller.Execute( "drop proc TEST.DataTableParameters;" ); } catch { /* don't care */ }
        try { controller.Execute( "drop table TEST.DataTableParameters;" ); } catch { /* don't care */ }
        try { controller.Execute( "drop type TEST.MyTVPType;" ); } catch { /* don't care */ }
        try { controller.Execute( "drop schema TEST;" ); } catch { /* don't care */ }

        try { controller.Execute( "create schema TEST;" ); } catch { /* don't care */ }
        controller.Execute( "create type TEST.MyTVPType as table (id int);" );
        controller.Execute( "create proc TEST.DataTableParameters @ids TEST.MyTVPType readonly as select count(1) from @ids" );

        var table = new DataTable { TableName = "TEST.MyTVPType", Columns = { { "id", typeof( int ) } }, Rows = { { 1 }, { 2 }, { 3 } } };
        table.SetTypeName( table.TableName ); // per SO29533765
        IDictionary<string, object> args = new Dictionary<string, object>
        {
            ["ids"] = table
        };
        int count = controller.Query<int>( "TEST.DataTableParameters", args, commandType: CommandType.StoredProcedure ).First();
        Assert.Equal( 3, count );

        count = controller.Query<int>( "select count(1) from @ids", args ).First();
        Assert.Equal( 3, count );
    }

    [Fact]
    public void SO26468710_InWithTVPs()
    {
        // this is just to make it re-runnable; normally you only do this once
        try { controller.Execute( "drop type MyIdList" ); }
        catch { /* don't care */ }
        controller.Execute( "create type MyIdList as table(id int);" );

        var ids = new DataTable
        {
            Columns = { { "id", typeof( int ) } },
            Rows = { { 1 }, { 3 }, { 5 } }
        };
        ids.SetTypeName( "MyIdList" );
        int sum = controller.Query<int>( @"
            declare @tmp table(id int not null);
            insert @tmp (id) values(1), (2), (3), (4), (5), (6), (7);
            select * from @tmp t inner join @ids i on i.id = t.id", new { ids } ).Sum();
        Assert.Equal( 9, sum );
    }

    [Fact]
    public void DataTableParametersWithExtendedProperty()
    {
        try { controller.Execute( "drop proc TEST.DataTableParameters;" ); } catch { /* don't care */ }
        try { controller.Execute( "drop table TEST.DataTableParameters;" ); } catch { /* don't care */ }
        try { controller.Execute( "drop type TEST.MyTVPType;" ); } catch { /* don't care */ }
        try { controller.Execute( "create schema TEST;" ); } catch { /* don't care */ }
        controller.Execute( "create type TEST.MyTVPType as table (id int);" );
        controller.Execute( "create proc TEST.DataTableParameters @ids TEST.MyTVPType readonly as select count(1) from @ids;" );

        var table = new DataTable { Columns = { { "id", typeof( int ) } }, Rows = { { 1 }, { 2 }, { 3 } } };
        table.SetTypeName( "TEST.MyTVPType" ); // <== extended metadata
        int count = controller.Query<int>( "TEST.DataTableParameters", new { ids = table }, commandType: CommandType.StoredProcedure ).First();
        Assert.Equal( 3, count );

        count = controller.Query<int>( "select count(1) from @ids", new { ids = table } ).First();
        Assert.Equal( 3, count );
        try
        {
            controller.Query<int>( "select count(1) from @ids", new { ids = table } ).First();
            throw new InvalidOperationException();
        }
        catch( Exception ex )
        {
            ex.Message.Equals( "The table type parameter 'ids' must have a valid type name." );
        }
    }

    [Fact]
    public void SupportInit()
    {
        var obj = controller.Query<WithInit>( "select 'abc' as Value" ).Single();
        Assert.Equal( "abc", obj.Value );
        Assert.Equal( 31, obj.Flags );
    }

    public class WithInit : ISupportInitialize
    {
        public string? Value { get; set; }
        public int Flags { get; set; }

        void ISupportInitialize.BeginInit() => Flags++;

        void ISupportInitialize.EndInit() => Flags += 30;
    }

    [Fact]
    public void SO29596645_TvpProperty()
    {
        try { controller.Execute( "create schema TEST;" ); } catch { /* don't care */ }
        try { controller.Execute( "CREATE TYPE TEST.SO29596645_ReminderRuleType AS TABLE (id int NOT NULL)" ); } catch { /* don't care */ }
        try { controller.Execute( "drop proc TEST.SO29596645_Proc" ); } catch { /* don't care */ }
        controller.Execute( @"create proc TEST.SO29596645_Proc (@Id int, @Rules TEST.SO29596645_ReminderRuleType READONLY)
                                as begin select @Id + ISNULL((select sum(id) from @Rules), 0); end" );
        var obj = new SO29596645_OrganisationDTO();
        int val = controller.Query<int>( "TEST.SO29596645_Proc", obj.Rules, commandType: CommandType.StoredProcedure ).Single();

        // 4 + 9 + 7 = 20
        Assert.Equal( 20, val );
    }

    private class SO29596645_RuleTableValuedParameters : SqlMapper.IDynamicParameters
    {
        private readonly string parameterName;

        public SO29596645_RuleTableValuedParameters( string parameterName )
        {
            this.parameterName = parameterName;
        }

        public void AddParameters( IDbCommand command, SqlMapper.Identity identity )
        {
            Debug.WriteLine( "> AddParameters" );
            var lazy = (SqlCommand)command;
            lazy.Parameters.AddWithValue( "Id", 7 );
            var table = new DataTable
            {
                Columns = { { "Id", typeof( int ) } },
                Rows = { { 4 }, { 9 } }
            };
            lazy.Parameters.AddWithValue( "Rules", table );
            Debug.WriteLine( "< AddParameters" );
        }
    }

    private class SO29596645_OrganisationDTO
    {
        public SO29596645_RuleTableValuedParameters Rules { get; }

        public SO29596645_OrganisationDTO()
        {
            Rules = new SO29596645_RuleTableValuedParameters( "@Rules" );
        }
    }
#endif

#if ENTITY_FRAMEWORK
    private class HazGeo
    {
        public int Id { get; set; }
        public DbGeography Geo { get; set; }
        public DbGeometry Geometry { get; set; }
    }

    private class HazSqlGeo
    {
        public int Id { get; set; }
        public SqlGeography Geo { get; set; }
        public SqlGeometry Geometry { get; set; }
    }

    [Fact]
    public void DBGeography_SO24405645_SO24402424()
    {
        EntityFramework.Handlers.Register();

        controller.Execute("create table #Geo (id int, geo geography, geometry geometry)");

        var obj = new HazGeo
        {
            Id = 1,
            Geo = DbGeography.LineFromText("LINESTRING(-122.360 47.656, -122.343 47.656 )", 4326),
            Geometry = DbGeometry.LineFromText("LINESTRING (100 100, 20 180, 180 180)", 0)
        };
        controller.Execute("insert #Geo(id, geo, geometry) values (@Id, @Geo, @Geometry)", obj);
        var row = controller.Query<HazGeo>("select * from #Geo where id=1").SingleOrDefault();
        Assert.NotNull(row);
        Assert.Equal(1, row.Id);
        Assert.NotNull(row.Geo);
        Assert.NotNull(row.Geometry);
    }

    [Fact]
    public void SqlGeography_SO25538154()
    {
        SqlMapper.ResetTypeHandlers();
        controller.Execute("create table #SqlGeo (id int, geo geography, geometry geometry)");

        var obj = new HazSqlGeo
        {
            Id = 1,
            Geo = SqlGeography.STLineFromText(new SqlChars(new SqlString("LINESTRING(-122.360 47.656, -122.343 47.656 )")), 4326),
            Geometry = SqlGeometry.STLineFromText(new SqlChars(new SqlString("LINESTRING (100 100, 20 180, 180 180)")), 0)
        };
        controller.Execute("insert #SqlGeo(id, geo, geometry) values (@Id, @Geo, @Geometry)", obj);
        var row = controller.Query<HazSqlGeo>("select * from #SqlGeo where id=1").SingleOrDefault();
        Assert.NotNull(row);
        Assert.Equal(1, row.Id);
        Assert.NotNull(row.Geo);
        Assert.NotNull(row.Geometry);
    }

    [Fact]
    public void NullableSqlGeometry()
    {
        SqlMapper.ResetTypeHandlers();
        controller.Execute("create table #SqlNullableGeo (id int, geometry geometry null)");

        var obj = new HazSqlGeo
        {
            Id = 1,
            Geometry = null
        };
        controller.Execute("insert #SqlNullableGeo(id, geometry) values (@Id, @Geometry)", obj);
        var row = controller.Query<HazSqlGeo>("select * from #SqlNullableGeo where id=1").SingleOrDefault();
        Assert.NotNull(row);
        Assert.Equal(1, row.Id);
        Assert.Null(row.Geometry);
    }

    [Fact]
    public void SqlHierarchyId_SO18888911()
    {
        SqlMapper.ResetTypeHandlers();
        var row = controller.Query<HazSqlHierarchy>("select 3 as [Id], hierarchyid::Parse('/1/2/3/') as [Path]").Single();
        Assert.Equal(3, row.Id);
        Assert.NotEqual(default(SqlHierarchyId), row.Path);

        var val = controller.Query<SqlHierarchyId>("select @Path", row).Single();
        Assert.NotEqual(default(SqlHierarchyId), val);
    }

    public class HazSqlHierarchy
    {
        public int Id { get; set; }
        public SqlHierarchyId Path { get; set; }
    }

#endif

    [Fact]
    public void TestCustomParameters()
    {
        var args = new DbParams {
            new SqlParameter("foo", 123),
            new SqlParameter("bar", "abc")
        };
        var result = controller.Query( "select Foo=@foo, Bar=@bar", args ).Single();
        int foo = result.Foo;
        string bar = result.Bar;
        Assert.Equal( 123, foo );
        Assert.Equal( "abc", bar );
    }

    [Fact]
    public void TestDynamicParamNullSupport()
    {
        var p = new DynamicParameters();

        p.Add( "@b", dbType: DbType.Int32, direction: ParameterDirection.Output );
        controller.Execute( "select @b = null", p );

        Assert.Null( p.Get<int?>( "@b" ) );
    }

    [Fact]
    public void TestAppendingAnonClasses()
    {
        var p = new DynamicParameters();
        p.AddDynamicParams( new { A = 1, B = 2 } );
        p.AddDynamicParams( new { C = 3, D = 4 } );

        var result = controller.Query( "select @A a,@B b,@C c,@D d", p ).Single();

        Assert.Equal( 1, (int)result.a );
        Assert.Equal( 2, (int)result.b );
        Assert.Equal( 3, (int)result.c );
        Assert.Equal( 4, (int)result.d );
    }

    [Fact]
    public void TestAppendingADictionary()
    {
        var dictionary = new Dictionary<string, object>
        {
            ["A"] = 1,
            ["B"] = "two"
        };

        var p = new DynamicParameters();
        p.AddDynamicParams( dictionary );

        var result = controller.Query( "select @A a, @B b", p ).Single();

        Assert.Equal( 1, (int)result.a );
        Assert.Equal( "two", (string)result.b );
    }

    [Fact]
    public void TestAppendingAnExpandoObject()
    {
        dynamic expando = new ExpandoObject();
        expando.A = 1;
        expando.B = "two";

        var p = new DynamicParameters();
        p.AddDynamicParams( expando );

        var result = controller.Query( "select @A a, @B b", p ).Single();

        Assert.Equal( 1, (int)result.a );
        Assert.Equal( "two", (string)result.b );
    }

    [Fact]
    public void TestAppendingAList()
    {
        var p = new DynamicParameters();
        var list = new int[] { 1, 2, 3 };
        p.AddDynamicParams( new { list } );

        var result = controller.Query<int>( "select * from (select 1 A union all select 2 union all select 3) X where A in @list", p ).ToList();

        Assert.Equal( 1, result[0] );
        Assert.Equal( 2, result[1] );
        Assert.Equal( 3, result[2] );
    }

    [Fact]
    public void TestAppendingAListAsDictionary()
    {
        var p = new DynamicParameters();
        var list = new int[] { 1, 2, 3 };
        var args = new Dictionary<string, object> { ["ids"] = list };
        p.AddDynamicParams( args );

        var result = controller.Query<int>( "select * from (select 1 A union all select 2 union all select 3) X where A in @ids", p ).ToList();

        Assert.Equal( 1, result[0] );
        Assert.Equal( 2, result[1] );
        Assert.Equal( 3, result[2] );
    }

    [Fact]
    public void TestAppendingAListByName()
    {
        DynamicParameters p = new DynamicParameters();
        var list = new int[] { 1, 2, 3 };
        p.Add( "ids", list );

        var result = controller.Query<int>( "select * from (select 1 A union all select 2 union all select 3) X where A in @ids", p ).ToList();

        Assert.Equal( 1, result[0] );
        Assert.Equal( 2, result[1] );
        Assert.Equal( 3, result[2] );
    }

    [Fact]
    public void ParameterizedInWithOptimizeHint()
    {
        const string sql = @"
select count(1)
from(
    select 1 as x
    union all select 2
    union all select 5) y
where y.x in @vals
option (optimize for (@vals unKnoWn))";
        int count = controller.Query<int>( sql, new { vals = new[] { 1, 2, 3, 4 } } ).Single();
        Assert.Equal( 2, count );

        count = controller.Query<int>( sql, new { vals = new[] { 1 } } ).Single();
        Assert.Equal( 1, count );

        count = controller.Query<int>( sql, new { vals = new int[0] } ).Single();
        Assert.Equal( 0, count );
    }

    [Fact]
    public void TestProcedureWithTimeParameter()
    {
        var p = new DynamicParameters();
        p.Add( "a", TimeSpan.FromHours( 10 ), dbType: DbType.Time );

        controller.Execute( @"CREATE PROCEDURE #TestProcWithTimeParameter
    @a TIME
    AS 
    BEGIN
    SELECT @a
    END" );
        Assert.Equal( controller.Query<TimeSpan>( "#TestProcWithTimeParameter", p, commandType: CommandType.StoredProcedure ).First(), new TimeSpan( 10, 0, 0 ) );
    }

    [Fact]
    public void TestUniqueIdentifier()
    {
        var guid = Guid.NewGuid();
        var result = controller.Query<Guid>( "declare @foo uniqueidentifier set @foo = @guid select @foo", new { guid } ).Single();
        Assert.Equal( guid, result );
    }

    [Fact]
    public void TestNullableUniqueIdentifierNonNull()
    {
        Guid? guid = Guid.NewGuid();
        var result = controller.Query<Guid?>( "declare @foo uniqueidentifier set @foo = @guid select @foo", new { guid } ).Single();
        Assert.Equal( guid, result );
    }

    [Fact]
    public void TestNullableUniqueIdentifierNull()
    {
        Guid? guid = null;
        var result = controller.Query<Guid?>( "declare @foo uniqueidentifier set @foo = @guid select @foo", new { guid } ).Single();
        Assert.Equal( guid, result );
    }

    [Fact]
    public void TestSupportForDynamicParameters()
    {
        var p = new DynamicParameters();
        p.Add( "name", "bob" );
        p.Add( "age", dbType: DbType.Int32, direction: ParameterDirection.Output );

        Assert.Equal( "bob", controller.Query<string>( "set @age = 11 select @name", p ).First() );
        Assert.Equal( 11, p.Get<int>( "age" ) );
    }

    [Fact]
    public void TestSupportForDynamicParametersOutputExpressions()
    {
        var bob = new Person { Name = "bob", PersonId = 1, Address = new Address { PersonId = 2 } };

        var p = new DynamicParameters( bob );
        p.Output( bob, b => b.PersonId );
        p.Output( bob, b => b.Occupation );
        p.Output( bob, b => b.NumberOfLegs );
        p.Output( bob, b => b.Address.Name );
        p.Output( bob, b => b.Address.PersonId );

        controller.Execute( @"
SET @Occupation = 'grillmaster' 
SET @PersonId = @PersonId + 1 
SET @NumberOfLegs = @NumberOfLegs - 1
SET @AddressName = 'bobs burgers'
SET @AddressPersonId = @PersonId", p );

        Assert.Equal( "grillmaster", bob.Occupation );
        Assert.Equal( 2, bob.PersonId );
        Assert.Equal( 1, bob.NumberOfLegs );
        Assert.Equal( "bobs burgers", bob.Address.Name );
        Assert.Equal( 2, bob.Address.PersonId );
    }

    [Fact]
    public void TestSupportForDynamicParametersOutputExpressions_Scalar()
    {
        using( var connection = GetOpenConnection() )
        {
            var bob = new Person { Name = "bob", PersonId = 1, Address = new Address { PersonId = 2 } };

            var p = new DynamicParameters( bob );
            p.Output( bob, b => b.PersonId );
            p.Output( bob, b => b.Occupation );
            p.Output( bob, b => b.NumberOfLegs );
            p.Output( bob, b => b.Address.Name );
            p.Output( bob, b => b.Address.PersonId );

            var result = (int?)controller.ExecuteScalar( @"
SET @Occupation = 'grillmaster' 
SET @PersonId = @PersonId + 1 
SET @NumberOfLegs = @NumberOfLegs - 1
SET @AddressName = 'bobs burgers'
SET @AddressPersonId = @PersonId
select 42", p );

            Assert.Equal( "grillmaster", bob.Occupation );
            Assert.Equal( 2, bob.PersonId );
            Assert.Equal( 1, bob.NumberOfLegs );
            Assert.Equal( "bobs burgers", bob.Address.Name );
            Assert.Equal( 2, bob.Address.PersonId );
            Assert.Equal( 42, result );
        }
    }

    [Fact]
    public void TestSupportForDynamicParametersOutputExpressions_Query_Buffered()
    {
        using( var connection = GetOpenConnection() )
        {
            var bob = new Person { Name = "bob", PersonId = 1, Address = new Address { PersonId = 2 } };

            var p = new DynamicParameters( bob );
            p.Output( bob, b => b.PersonId );
            p.Output( bob, b => b.Occupation );
            p.Output( bob, b => b.NumberOfLegs );
            p.Output( bob, b => b.Address.Name );
            p.Output( bob, b => b.Address.PersonId );

            var result = controller.Query<int>( @"
SET @Occupation = 'grillmaster' 
SET @PersonId = @PersonId + 1 
SET @NumberOfLegs = @NumberOfLegs - 1
SET @AddressName = 'bobs burgers'
SET @AddressPersonId = @PersonId
select 42", p, buffered: true ).Single();

            Assert.Equal( "grillmaster", bob.Occupation );
            Assert.Equal( 2, bob.PersonId );
            Assert.Equal( 1, bob.NumberOfLegs );
            Assert.Equal( "bobs burgers", bob.Address.Name );
            Assert.Equal( 2, bob.Address.PersonId );
            Assert.Equal( 42, result );
        }
    }

    [Fact]
    public void TestSupportForDynamicParametersOutputExpressions_Query_NonBuffered()
    {
        using( var connection = GetOpenConnection() )
        {
            var bob = new Person { Name = "bob", PersonId = 1, Address = new Address { PersonId = 2 } };

            var p = new DynamicParameters( bob );
            p.Output( bob, b => b.PersonId );
            p.Output( bob, b => b.Occupation );
            p.Output( bob, b => b.NumberOfLegs );
            p.Output( bob, b => b.Address.Name );
            p.Output( bob, b => b.Address.PersonId );

            var result = controller.Query<int>( @"
SET @Occupation = 'grillmaster' 
SET @PersonId = @PersonId + 1 
SET @NumberOfLegs = @NumberOfLegs - 1
SET @AddressName = 'bobs burgers'
SET @AddressPersonId = @PersonId
select 42", p, buffered: false ).Single();

            Assert.Equal( "grillmaster", bob.Occupation );
            Assert.Equal( 2, bob.PersonId );
            Assert.Equal( 1, bob.NumberOfLegs );
            Assert.Equal( "bobs burgers", bob.Address.Name );
            Assert.Equal( 2, bob.Address.PersonId );
            Assert.Equal( 42, result );
        }
    }

    [Fact]
    public void TestSupportForDynamicParametersOutputExpressions_QueryMultiple()
    {
        using( var connection = GetOpenConnection() )
        {
            var bob = new Person { Name = "bob", PersonId = 1, Address = new Address { PersonId = 2 } };

            var p = new DynamicParameters( bob );
            p.Output( bob, b => b.PersonId );
            p.Output( bob, b => b.Occupation );
            p.Output( bob, b => b.NumberOfLegs );
            p.Output( bob, b => b.Address.Name );
            p.Output( bob, b => b.Address.PersonId );

            int x, y;
            using( var multi = controller.QueryMultiple( @"
SET @Occupation = 'grillmaster' 
SET @PersonId = @PersonId + 1 
SET @NumberOfLegs = @NumberOfLegs - 1
SET @AddressName = 'bobs burgers'
select 42
select 17
SET @AddressPersonId = @PersonId", p ) )
            {
                x = multi.Read<int>().Single();
                y = multi.Read<int>().Single();
            }

            Assert.Equal( "grillmaster", bob.Occupation );
            Assert.Equal( 2, bob.PersonId );
            Assert.Equal( 1, bob.NumberOfLegs );
            Assert.Equal( "bobs burgers", bob.Address.Name );
            Assert.Equal( 2, bob.Address.PersonId );
            Assert.Equal( 42, x );
            Assert.Equal( 17, y );
        }
    }

    [Fact]
    public void TestSupportForExpandoObjectParameters()
    {
        dynamic p = new ExpandoObject();
        p.name = "bob";
        object parameters = p;
        string result = controller.Query<string>( "select @name", parameters ).First();
        Assert.Equal( "bob", result );
    }

    [Fact]
    public void SO25069578_DynamicParams_Procs()
    {
        using( var ctx = new SqlTransactionCallContext( TestHelper.Monitor ) )
        {
            var controller = ctx[TestHelper.GetConnectionString()];
            var parameters = new DynamicParameters();
            parameters.Add( "foo", "bar" );
            // parameters = new DynamicParameters(parameters);
            try { controller.Execute( "drop proc SO25069578" ); }
            catch { /* don't care */ }
            controller.Execute( "create proc SO25069578 @foo nvarchar(max) as select @foo as [X]" );
            var tran = controller.BeginTransaction(); // gist used transaction; behaves the same either way, though
            var row = controller.Query<HazX>( "SO25069578", parameters,
                commandType: CommandType.StoredProcedure ).Single();
            tran.RollbackAll();
            Assert.Equal( "bar", row.X );
        }
    }

    public class HazX
    {
        public string? X { get; set; }
    }

    [Fact]
    public void SO25297173_DynamicIn()
    {
        const string query = @"
declare @table table(value int not null);
insert @table values(1);
insert @table values(2);
insert @table values(3);
insert @table values(4);
insert @table values(5);
insert @table values(6);
insert @table values(7);
SELECT value FROM @table WHERE value IN @myIds";
        var queryParams = new Dictionary<string, object>
        {
            ["myIds"] = new[] { 5, 6 }
        };

        var dynamicParams = new DynamicParameters( queryParams );
        List<int> result = controller.Query<int>( query, dynamicParams ).ToList();
        Assert.Equal( 2, result.Count );
        Assert.Contains( 5, result );
        Assert.Contains( 6, result );
    }

    [Fact]
    public void Test_AddDynamicParametersRepeatedShouldWork()
    {
        var args = new DynamicParameters();
        args.AddDynamicParams( new { Foo = 123 } );
        args.AddDynamicParams( new { Foo = 123 } );
        int i = controller.Query<int>( "select @Foo", args ).Single();
        Assert.Equal( 123, i );
    }

    [Fact]
    public void Test_AddDynamicParametersRepeatedIfParamTypeIsDbStiringShouldWork()
    {
        var foo = new DbString() { Value = "123" };

        var args = new DynamicParameters();
        args.AddDynamicParams( new { Foo = foo } );
        args.AddDynamicParams( new { Foo = foo } );
        int i = controller.Query<int>( "select @Foo", args ).Single();
        Assert.Equal( 123, i );
    }

    [Fact]
    public void AllowIDictionaryParameters()
    {
        var parameters = new Dictionary<string, object>
        {
            ["param1"] = 0
        };

        controller.Query( "SELECT @param1", parameters );
    }

    [Fact]
    public void TestParameterWithIndexer()
    {
        controller.Execute( @"create proc #TestProcWithIndexer 
	@A int
as 
begin
	select @A
end" );
        var item = controller.Query<int>( "#TestProcWithIndexer", new ParameterWithIndexer(), commandType: CommandType.StoredProcedure ).Single();
    }

    public class ParameterWithIndexer
    {
        public int A { get; set; }
        public virtual string? this[string columnName]
        {
            get { return null; }
            set { }
        }
    }

    [Fact]
    public void TestMultipleParametersWithIndexer()
    {
        var order = controller.Query<MultipleParametersWithIndexer>( "select 1 A,2 B" ).First();

        Assert.Equal( 1, order.A );
        Assert.Equal( 2, order.B );
    }

    public class MultipleParametersWithIndexer : MultipleParametersWithIndexerDeclaringType
    {
        public int A { get; set; }
    }

    public class MultipleParametersWithIndexerDeclaringType
    {
        public object? this[object field]
        {
            get { return null; }
            set { }
        }

        public object? this[object field, int index]
        {
            get { return null; }
            set { }
        }

        public int B { get; set; }
    }

    [Fact]
    public void Issue182_BindDynamicObjectParametersAndColumns()
    {
        controller.Execute( "create table #Dyno ([Id] uniqueidentifier primary key, [Name] nvarchar(50) not null, [Foo] bigint not null);" );

        var guid = Guid.NewGuid();
        var orig = new Dyno { Name = "T Rex", Id = guid, Foo = 123L };
        var result = controller.Execute( "insert into #Dyno ([Id], [Name], [Foo]) values (@Id, @Name, @Foo);", orig );

        var fromDb = controller.Query<Dyno>( "select * from #Dyno where Id=@Id", orig ).Single();
        Assert.Equal( (Guid)fromDb.Id, guid );
        Assert.Equal( "T Rex", fromDb.Name );
        Assert.Equal( 123L, (long?)fromDb.Foo );
    }

    public class Dyno
    {
        public dynamic? Id { get; set; }
        public string? Name { get; set; }

        public object? Foo { get; set; }
    }

    [Fact]
    public void Issue151_ExpandoObjectArgsQuery()
    {
        dynamic args = new ExpandoObject();
        args.Id = 123;
        args.Name = "abc";

        var row = controller.Query( "select @Id as [Id], @Name as [Name]", (object)args ).Single();
        Assert.Equal( 123, (int)row.Id );
        Assert.Equal( "abc", (string)row.Name );
    }

    [Fact]
    public void Issue151_ExpandoObjectArgsExec()
    {
        dynamic args = new ExpandoObject();
        args.Id = 123;
        args.Name = "abc";
        controller.Execute( "create table #issue151 (Id int not null, Name nvarchar(20) not null)" );
        Assert.Equal( 1, controller.Execute( "insert #issue151 values(@Id, @Name)", (object)args ) );
        var row = controller.Query( "select Id, Name from #issue151" ).Single();
        Assert.Equal( 123, (int)row.Id );
        Assert.Equal( "abc", (string)row.Name );
    }

    [Fact]
    public void Issue192_InParameterWorksWithSimilarNames()
    {
        var rows = controller.Query( @"
declare @Issue192 table (
    Field INT NOT NULL PRIMARY KEY IDENTITY(1,1),
    Field_1 INT NOT NULL);
insert @Issue192(Field_1) values (1), (2), (3);
SELECT * FROM @Issue192 WHERE Field IN @Field AND Field_1 IN @Field_1",
new { Field = new[] { 1, 2 }, Field_1 = new[] { 2, 3 } } ).Single();
        Assert.Equal( 2, (int)rows.Field );
        Assert.Equal( 2, (int)rows.Field_1 );
    }

    [Fact]
    public void Issue192_InParameterWorksWithSimilarNamesWithUnicode()
    {
        var rows = controller.Query( @"
declare @Issue192 table (
    Field INT NOT NULL PRIMARY KEY IDENTITY(1,1),
    Field_1 INT NOT NULL);
insert @Issue192(Field_1) values (1), (2), (3);
SELECT * FROM @Issue192 WHERE Field IN @µ AND Field_1 IN @µµ",
new { µ = new[] { 1, 2 }, µµ = new[] { 2, 3 } } ).Single();
        Assert.Equal( 2, (int)rows.Field );
        Assert.Equal( 2, (int)rows.Field_1 );
    }

    [FactUnlessCaseSensitiveDatabase]
    public void Issue220_InParameterCanBeSpecifiedInAnyCase()
    {
        // note this might fail if your database server is case-sensitive
        Assert.Equal(
            new[] { 1 },
            controller.Query<int>( "select * from (select 1 as Id) as X where Id in @ids", new { Ids = new[] { 1 } } )
        );
    }

    [Fact]
    public void SO30156367_DynamicParamsWithoutExec()
    {
        var dbParams = new DynamicParameters();
        dbParams.Add( "Field1", 1 );
        var value = dbParams.Get<int>( "Field1" );
        Assert.Equal( 1, value );
    }

    [Fact]
    public void RunAllStringSplitTestsDisabled()
    {
        RunAllStringSplitTests( -1, 1500 );
    }

    [FactRequiredCompatibilityLevel( FactRequiredCompatibilityLevelAttribute.SqlServer2016 )]
    public void RunAllStringSplitTestsEnabled()
    {
        RunAllStringSplitTests( 10, 4500 );
    }

    private void RunAllStringSplitTests( int stringSplit, int max = 150 )
    {
        int oldVal = SqlMapper.Settings.InListStringSplitCount;
        try
        {
            SqlMapper.Settings.InListStringSplitCount = stringSplit;
            try { controller.Execute( "drop table #splits" ); } catch { /* don't care */ }
            int count = controller.QuerySingle<int>( "create table #splits (i int not null);"
                + string.Concat( Enumerable.Range( -max, max * 3 ).Select( i => $"insert #splits (i) values ({i});" ) )
                + "select count(1) from #splits" );
            Assert.Equal( count, 3 * max );

            for( int i = 0; i < max; Incr( ref i ) )
            {
                try
                {
                    var vals = Enumerable.Range( 1, i );
                    var list = controller.Query<int>( "select i from #splits where i in @vals", new { vals } ).AsList();
                    Assert.Equal( list.Count, i );
                    Assert.Equal( list.Sum(), vals.Sum() );
                }
                catch( Exception ex )
                {
                    throw new InvalidOperationException( $"Error when i={i}: {ex.Message}", ex );
                }
            }
        }
        finally
        {
            SqlMapper.Settings.InListStringSplitCount = oldVal;
        }
    }

    private static void Incr( ref int i )
    {
        if( i <= 15 ) i++;
        else if( i <= 80 ) i += 5;
        else if( i <= 200 ) i += 10;
        else if( i <= 1000 ) i += 50;
        else i += 100;
    }

    [Fact]
    public void Issue601_InternationalParameterNamesWork()
    {
        // regular parameter
        var result = controller.QuerySingle<int>( "select @æøå٦", new { æøå٦ = 42 } );
        Assert.Equal( 42, result );
    }

    [Fact]
    public void TestListExpansionPadding_Enabled() => TestListExpansionPadding( true );

    [Fact]
    public void TestListExpansionPadding_Disabled() => TestListExpansionPadding( false );

    private void TestListExpansionPadding( bool enabled )
    {
        bool oldVal = SqlMapper.Settings.PadListExpansions;
        try
        {
            SqlMapper.Settings.PadListExpansions = enabled;
            Assert.Equal( 4096, controller.ExecuteScalar<int>( @"
create table #ListExpansion(id int not null identity(1,1), value int null);
insert #ListExpansion (value) values (null);
declare @loop int = 0;
while (@loop < 12)
begin -- double it
	insert #ListExpansion (value) select value from #ListExpansion;
	set @loop = @loop + 1;
end

select count(1) as [Count] from #ListExpansion" ) );

            var list = new List<int>();
            int nextId = 1, batchCount;
            var rand = new Random( 12345 );
            const int SQL_SERVER_MAX_PARAMS = 2095;
            TestListForExpansion( list, enabled ); // test while empty
            while( list.Count < SQL_SERVER_MAX_PARAMS )
            {
                try
                {
                    if( list.Count <= 20 ) batchCount = 1;
                    else if( list.Count <= 200 ) batchCount = rand.Next( 1, 40 );
                    else batchCount = rand.Next( 1, 100 );

                    for( int j = 0; j < batchCount && list.Count < SQL_SERVER_MAX_PARAMS; j++ )
                        list.Add( nextId++ );

                    TestListForExpansion( list, enabled );
                }
                catch( Exception ex )
                {
                    throw new InvalidOperationException( $"Failure with {list.Count} items: {ex.Message}", ex );
                }
            }
        }
        finally
        {
            SqlMapper.Settings.PadListExpansions = oldVal;
        }
    }

    private void TestListForExpansion( List<int> list, bool enabled )
    {
        var row = controller.QuerySingle( @"
declare @hits int, @misses int, @count int;
select @count = count(1) from #ListExpansion;
select @hits = count(1) from #ListExpansion where id in @ids ;
select @misses = count(1) from #ListExpansion where not id in @ids ;
declare @query nvarchar(max) = N' in @ids '; -- ok, I confess to being pleased with this hack ;p
select @hits as [Hits], (@count - @misses) as [Misses], @query as [Query];
", new { ids = list } );
        int hits = row.Hits, misses = row.Misses;
        string query = row.Query;
        int argCount = Regex.Matches( query, "@ids[0-9]" ).Count;
        int expectedCount = GetExpectedListExpansionCount( list.Count, enabled );
        Assert.Equal( hits, list.Count );
        Assert.Equal( misses, list.Count );
        Assert.Equal( argCount, expectedCount );
    }

    private static int GetExpectedListExpansionCount( int count, bool enabled )
    {
        if( !enabled ) return count;

        if( count <= 5 || count > 2070 ) return count;

        int padFactor;
        if( count <= 150 ) padFactor = 10;
        else if( count <= 750 ) padFactor = 50;
        else if( count <= 2000 ) padFactor = 100;
        else if( count <= 2070 ) padFactor = 10;
        else padFactor = 200;

        int blocks = count / padFactor, delta = count % padFactor;
        if( delta != 0 ) blocks++;
        return blocks * padFactor;
    }
}
