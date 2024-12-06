using ActCourse.Backend.Data;
using ActCourse.Backend.DTO;
using ActCourse.Backend.Models;
using ActCourse.Backend.Services;
using ActCourse.Backend.Settings;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

//add DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

//DI
builder.Services.AddScoped<ICategory, CategoryData>();
builder.Services.AddScoped<ICourse, CourseData>();

//Automapper
builder.Services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());
builder.Services.AddSingleton<TokenService>();

var secretKey = ApiSettings.GenerateSecretByte();

//Add Identity
builder.Services.AddIdentity<IdentityUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme()
    {
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

//Add Jwt Token

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = true;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(secretKey),
        ValidateIssuer = false,
        ValidateAudience = false
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("admin"));
    options.AddPolicy("Dosen", policy => policy.RequireRole("dosen"));
    options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();

});


var app = builder.Build();

app.MapDefaultEndpoints();


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

//Category API

//GET /api/categories

app.MapGet("/api/categories", async (ICategory categoryData, IMapper mapper) =>
{
    var categories = await categoryData.GetAll();
    var categoriesDto = mapper.Map<IEnumerable<CategoryDTO>>(categories);
    return Results.Ok(categoriesDto);
}).RequireAuthorization("Admin");

//GET /api/categories/{id}
app.MapGet("/api/categories/{id}", async (ICategory categoryData, IMapper mapper, int id) =>
{
    try
    {
        var category = await categoryData.GetById(id);
        var categoryDto = mapper.Map<CategoryDTO>(category);
        return Results.Ok(categoryDto);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

//POST /api/categories
app.MapPost("/api/categories", async (ICategory categoryData, IMapper mapper,
    CategoryAddDTO categoryAddDTO) =>
{
    var category = mapper.Map<Category>(categoryAddDTO);
    try
    {
        var newCategory = await categoryData.Add(category);
        var categoryDto = mapper.Map<CategoryDTO>(newCategory);
        return Results.Created($"/api/categories/{categoryDto.CategoryId}", categoryDto);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
});


//PUT /api/categories/{id}
app.MapPut("/api/categories/{id}", async (ICategory categoryData, IMapper mapper,
    int id, CategoryUpdateDTO categoryUpdateDTO) =>
{
    var category = mapper.Map<Category>(categoryUpdateDTO);
    category.CategoryId = id;
    try
    {
        var updatedCategory = await categoryData.Update(category);
        var categoryDto = mapper.Map<CategoryDTO>(updatedCategory);
        return Results.Ok(categoryDto);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

//DELETE /api/categories/{id}
app.MapDelete("/api/categories/{id}", async (ICategory categoryData, IMapper mapper, int id) =>
{
    try
    {
        var category = await categoryData.Delete(id);
        var categoryDto = mapper.Map<CategoryDTO>(category);
        return Results.Ok(categoryDto);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
});


//add new user
app.MapPost("/api/users", async (UserManager<IdentityUser> userManager, IMapper mapper,
    UserAddDTO userAddDTO) =>
{
    var user = mapper.Map<IdentityUser>(userAddDTO);

    try
    {
        var result = await userManager.CreateAsync(user, userAddDTO.Password);
        if (result.Succeeded)
        {
            UserDTO userDTO = mapper.Map<UserDTO>(user);
            return Results.Created($"/api/users/{user.Id}", userDTO);
        }
        else
        {
            return Results.BadRequest(result.Errors);
        }
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

//search user by id
app.MapGet("/api/users/{id}", async (UserManager<IdentityUser> userManager, IMapper mapper, string id) =>
{
    var user = await userManager.FindByIdAsync(id);
    if (user == null)
    {
        return Results.NotFound();
    }
    UserDTO userDTO = mapper.Map<UserDTO>(user);
    return Results.Ok(userDTO);
});


//create login page and return jwt token
app.MapPost("/api/login", async (UserManager<IdentityUser> userManager, IMapper mapper, TokenService tokenService,
    UserLoginDTO userLoginDTO) =>
{
    var user = await userManager.FindByEmailAsync(userLoginDTO.UserName);
    if (user == null)
    {
        return Results.NotFound();
    }
    var result = await userManager.CheckPasswordAsync(user, userLoginDTO.Password);
    if (result)
    {
        var token = await tokenService.GenerateToken(userManager, user);
        UserWithTokenDTO userWithTokenDTO = new UserWithTokenDTO
        {
            Email = user.Email,
            UserName = user.UserName,
            Token = token
        };
        return Results.Ok(userWithTokenDTO);
    }
    return Results.BadRequest("Invalid login attempt");
});

//add role
app.MapPost("/api/roles", async (RoleManager<IdentityRole> roleManager, RoleAddDTO roleAddDTO) =>
{
    var role = new IdentityRole
    {
        Name = roleAddDTO.Name
    };
    var result = await roleManager.CreateAsync(role);
    if (result.Succeeded)
    {
        RoleDTO roleDTO = new RoleDTO
        {
            Id = role.Id,
            Name = role.Name
        };
        return Results.Created($"/api/roles/{role.Id}", roleDTO);
    }
    return Results.BadRequest(result.Errors);
});

//get role by id
app.MapGet("/api/roles/{id}", async (RoleManager<IdentityRole> roleManager, string id) =>
{
    var role = await roleManager.FindByIdAsync(id);
    if (role == null)
    {
        return Results.NotFound();
    }
    RoleDTO roleDTO = new RoleDTO
    {
        Id = id,
        Name = role.Name
    };
    return Results.Ok(roleDTO);
});

//register user to role
app.MapPost("/api/registeruserrole", async (UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager,
     RegisterUserRoleDto registerUserRoleDto) =>
{
    var user = await userManager.FindByNameAsync(registerUserRoleDto.UserName);
    if (user == null)
    {
        return Results.NotFound();
    }
    var role = await roleManager.FindByNameAsync(registerUserRoleDto.RoleName);
    if (role == null)
    {
        return Results.NotFound();
    }
    var result = await userManager.AddToRoleAsync(user, role.Name);
    if (result.Succeeded)
    {
        return Results.Ok();
    }
    return Results.BadRequest(result.Errors);
});


// Course API
// GET List of Course
app.MapGet("/api/courses", async (ICourse courseData, IMapper mapper) =>
{
    var courses = await courseData.GetAll();
    var coursesDto = mapper.Map<IEnumerable<CourseDTO>>(courses);
    return Results.Ok(coursesDto);
});

app.MapGet("/api/courses/{id}", async (ICourse courseData, IMapper mapper, int id) =>
{
    try
    {
        var course = await courseData.GetById(id);
        var courseDto = mapper.Map<CourseDTO>(course);
        return Results.Ok(courseDto);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

app.MapPost("/api/courses", async (ICourse courseData, IMapper mapper, CourseAddDTO courseAddDTO) =>
{
    var course = mapper.Map<Course>(courseAddDTO);
    try
    {
        var newCourse = await courseData.Add(course);
        var courseDto = mapper.Map<CourseDTO>(newCourse);
        return Results.Created($"/api/courses/{courseDto.CourseId}", courseDto);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

app.MapPut("/api/courses/{id}", async (ICourse courseData, IMapper mapper, int id, CourseUpdateDTO courseUpdateDTO) =>
{
    var course = mapper.Map<Course>(courseUpdateDTO);
    course.CourseId = id;
    try
    {
        var updatedCourse = await courseData.Update(course);
        var courseDto = mapper.Map<CourseDTO>(updatedCourse);
        return Results.Ok(courseDto);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

app.MapDelete("/api/courses/{id}", async (ICourse courseData, IMapper mapper, int id) =>
{
    try
    {
        var course = await courseData.Delete(id);
        var courseDto = mapper.Map<CourseDTO>(course);
        return Results.Ok(courseDto);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex.Message);
    }
});
app.Run();