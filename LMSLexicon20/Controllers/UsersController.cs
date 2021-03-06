﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using LMSLexicon20.Data;
using LMSLexicon20.Extensions;
using LMSLexicon20.Filters;
using LMSLexicon20.Models;
using LMSLexicon20.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Internal;


namespace LMSLexicon20.Controllers
{
    public class UsersController : Controller
    {

        private ApplicationDbContext _context;
        private UserManager<User> _userManager;
        private IMapper _mapper;

        public UsersController(ApplicationDbContext dbContext, UserManager<User> userManager, IMapper mapper)
        {
            _context = dbContext;
            _userManager = userManager;
            _mapper = mapper;
        }

        public IActionResult Index()
        {
            return View();
        }
        [Authorize]
        public IActionResult Start()
        {
            var id = _userManager.GetUserId(User);
            if (User.IsInRole("Teacher"))
            {
                return RedirectToAction(nameof(TeacherIndex), new { id = id });
            }
            else if (User.IsInRole("Student"))
            {
                return RedirectToAction(nameof(StudentIndex), new { id = id });
            }
            return View();
        }
        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> TeacherIndex(string id)
        {

            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }
            var viewModel = _mapper.Map<TeacherIndexViewModel>(user);

            if (user.CourseId != null)
                viewModel.Course = await _context.Courses
                    .Include(c => c.Modules)
                    .FirstOrDefaultAsync(c => c.Id == user.CourseId);

            viewModel.StudentsInCourse = _userManager.GetUsersInRoleAsync("Student").Result
                            .Where(u => u.CourseId == user.CourseId).Count();

            viewModel.CurrentModule = viewModel.Course.Modules
                .FirstOrDefault(m => m.StartDate <= DateTime.Now.Date
                    && m.EndDate >= DateTime.Now.Date);

            viewModel.Assignments = await _context.Activities
                .Where(a => a.Module.CourseId == user.CourseId)
                .Where(a => a.HasDeadline == true)
                .Include(a => a.Documents)
                .ThenInclude(d => d.User)
                .OrderBy(a => a.EndDate)
                .ToListAsync();


            var now = DateTime.Now;

            var today = DateTime.Now;
            //starta veckan med närmaste föregående måndag
            //räkna hur många dagar det var sedan 
            //ons=3 idag+(-3+1) => idag+-2 dgr =måndag(1)
            viewModel.StartOfWeek = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);

            //alla activities i kursen
            viewModel.WeeklyActivities = await _context.Activities
               .Where(a => a.Module.CourseId == user.CourseId)
               .Where(a => viewModel.StartOfWeek.AddDays(6)>=a.StartDate.Date && viewModel.StartOfWeek.Date <=a.EndDate  )
               .ToListAsync();

            //alla som startar innan sjunde dagen och slutar efter första dagen i veckan
            //.Where(a =>
            //     now.AddDays(6) > a.StartDate
            //     && now < a.EndDate)

            return View(viewModel);
        }
        public async Task<IActionResult> ShowDocuments(int id)
        {
            var activity = await _context.Activities
                .Include(a => a.Documents)
                .ThenInclude(d => d.User)
                .FirstOrDefaultAsync(a => a.Id == id);

            //endast inlämningar från elever
            if (activity.Documents == null)
                return NotFound();
            var documents = activity.Documents
                .GroupBy(d => d.UserId)
                .Select(d => d.First())
                .ToList();

            var model = new ShowDocuments
            {
                Id = id,
                Documents = documents
            };
            if (model == null)
                return NotFound();
            if (Request.IsAjax())
                return PartialView("ShowDocumentsPartial", model);
            return View(model);
        }
        public async Task<IActionResult> StudentIndex(string id)
        {
            var user = await _context.Users.FindAsync(id);
            //var viewModel = _mapper.Map<StudentIndexViewModel>(user);
            var viewModel = _mapper.ProjectTo<StudentIndexViewModel>(_userManager.Users).FirstOrDefault(e => e.Id == id);
            var documents = await _context.Documents.Where(e => e.UserId == id).ToListAsync();

            viewModel.Documents = documents;

            var today = DateTime.Today;
            var mondayWeekStartDay = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);

            var activities = _context.Activities.Include(e => e.ActivityType).Include(e => e.Module).Where(e => e.Module.CourseId == user.CourseId);
            viewModel.WeeklyActivities = await activities.Where(e => e.StartDate.Date >= mondayWeekStartDay && e.StartDate.Date < mondayWeekStartDay.AddDays(6)).ToListAsync();

            var assignments = await activities.Include(e => e.Documents).Where(e => e.ActivityType.RequireDocument == true && e.StartDate.Date <= today).ToListAsync();
            var openAssignment = new List<Activity>();

            foreach (var item in assignments)
            {
                openAssignment.Add(item);
            }

            foreach (var assignment in assignments)
            {
                foreach (var document in documents)
                {
                    if (assignment.Id == document.ActivityId)
                    {
                        openAssignment.Remove(assignment);
                    }
                }
            }
            viewModel.OpenAssignments = openAssignment;
            return View(viewModel);
        }


        // GET: User/Create
        [Authorize(Roles = "Teacher")]
        public ActionResult CreateUser(int? courseId = null)
        {
            //TempData["courseId"] = id;
            //return View();
            if (courseId != null)
            {
                var courseExists = _context.Courses.Any(c => c.Id == courseId);
                if (!courseExists)
                    throw new Exception("Du försökte gå till en kurs som inte finns!");
            }
            return View();
        }

        //POST: User/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> CreateUser(CreateUserViewModel viewModel, int? id = null)
        {
            if (ModelState.IsValid)
            {
                //Hämta användare
                var model = _mapper.Map<User>(viewModel);
                var user = await _userManager.FindByNameAsync(model.UserName);
                //Bör aldrig vara null - checkar ändå
                if (user != null) throw new Exception("Användaren finns redan");

                //Lägg till kurs om finns (checkat att den finns)
                if (id != null) model.CourseId = id;

                //Lägg till användare m. lösen
                var pw = GeneratePassword();

                var addUserResult = await _userManager.CreateAsync(model, pw);
                if (!addUserResult.Succeeded) throw new Exception(string.Join("\n", addUserResult.Errors));

                //Lägg till roll
                var addRoleResult = id == null ?
                await _userManager.AddToRoleAsync(model, "Teacher") :        //true=teacher
                await _userManager.AddToRoleAsync(model, "Student");         //false=student
                if (!addRoleResult.Succeeded) throw new Exception(string.Join("\n", addRoleResult.Errors));

                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(CreateUserConfirmed), new { userName = model.UserName, pw = pw });
            }
            return View(viewModel);

        }
        [Authorize(Roles = "Teacher")]
        public IActionResult CreateUserConfirmed(string userName, string pw)
        {
            TempData["pw"] = pw;
            TempData["userName"] = userName;
            return View();
        }
        //Get
        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> Edit(string id)
        {

            var model = await _context.Users.FindAsync(id);
            if (model == null)
                NotFound();
            var isStudent = await _userManager.IsInRoleAsync(model, "Student");
            var viewModel = _mapper.Map<UserEditViewModel>(model);
            if (isStudent)
            {
                viewModel.Student = isStudent;
                ViewData["Course"] = new SelectList(_context.Set<Course>(), "Id", "Name", model.CourseId);
            }
            return View(viewModel);
        }
        //Post
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> Edit(UserEditViewModel viewModel, string id)
        {
            var found = await _context.Users
                .AnyAsync(p => (p.Email == viewModel.Email)
                && (p.Id != id));
            if (found)
            {
                ModelState.AddModelError("Email", "Emailen används redan!");
            }

            viewModel.Id = id;
            if (ModelState.IsValid)
            {
                var model = await _context.Users.FindAsync(id);

                model.Email = viewModel.Email;
                model.FirstName = viewModel.FirstName;
                model.LastName = viewModel.LastName;
                model.PhoneNumber = viewModel.PhoneNumber;
                model.Course = await _context.Courses.FindAsync(viewModel.CourseId);

                try
                {
                    _context.Update(model);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserExists(model.Id))
                        return NotFound();
                    else
                        throw;
                }
                TempData["SuccessText"] = $"Användare {model.Email} har uppdaterats";
                return RedirectToAction(nameof(List));
            }
            return View(viewModel);
        }

        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var model = await _context.Users.FindAsync(id);
            if (model == null)
                NotFound();
            var viewModel = _mapper.Map<UserDeleteViewModel>(model);
            var role = await _userManager.IsInRoleAsync(await _userManager.FindByIdAsync(id), "Teacher");
            viewModel.Role = role ? "Lärare" : "Elev";
            // viewModel.Course = model.Course;
            return View(viewModel);
        }

        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> DeleteUserConfirmed(string id)
        {
            var model = await _context.Users.FindAsync(id);
            if (User.Identity.Name == model.UserName)
                throw new Exception("Du kan inte ta bort kontot du använder!");
            else
            {
                var userName = model.UserName;
                _context.Users.Remove(model);
                await _context.SaveChangesAsync();
                TempData["SuccessText"] = $"Användare {userName} har tagits bort";
            }

            return RedirectToAction(nameof(List), new { filterSearch = "" });
        }
        static string GeneratePassword()
        {
            //ToDo: check final string
            var length = 15;
            string validChars = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*?_-";
            //var validUpperCases = "ABCDEFGHJKLMNOPQRSTUVWXYZ";
            //var validLowerCases = "abcdefghijklmnopqrstuvwxyz";
            //var validNumbers = "0123456789";
            //var validSpecial = "!@#$%^&*?_-";

            Random random = new Random();

            char[] chars = new char[length];

            for (int i = 0; i < length; i++)
            {
                chars[i] = validChars[random.Next(0, validChars.Length)];
            }
            return new string(chars);
        }

        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> List(string filterSearch, string sortOrder)
        {
            var viewModel = await _mapper.ProjectTo<UserListViewModel>(_userManager.Users).ToListAsync();
            for (int i = 0; i < viewModel.Count; i++)
            {
                //var role = await _userManager.GetRolesAsync(await _userManager.FindByIdAsync(viewModel[i].Id));
                //viewModel[i].UserRole = role[0] == "Teacher" ? "Lärare" : "Elev";
                var role = await _userManager.IsInRoleAsync(await _userManager.FindByIdAsync(viewModel[i].Id), "Teacher");
                viewModel[i].UserRole = role ? "Lärare" : "Elev";
            }

            var filter = string.IsNullOrWhiteSpace(filterSearch) ?
                            viewModel : viewModel.Where(m => m.FullName.ToLower().Contains(filterSearch.ToLower()) ||
                                                                m.Email.ToLower().Contains(filterSearch.ToLower()));

            ViewData["OptionOne"] = sortOrder == "Role" ? "role_desc" : "Role";
            ViewData["OptionTwo"] = sortOrder == "name_desc" ? "Name" : "name_desc";
            ViewData["OptionThree"] = sortOrder == "Email" ? "email_desc" : "Email";
            ViewData["OptionFour"] = sortOrder == "Course" ? "course_desc" : "Course";

            filter = sortOrder switch
            {
                "Email" => filter.OrderBy(s => s.Email),
                "email_desc" => filter.OrderByDescending(s => s.Email),
                "Role" => filter.OrderBy(s => s.UserRole),
                "role_desc" => filter.OrderByDescending(s => s.UserRole),
                "Course" => filter.OrderBy(s => s.CourseName),
                "course_desc" => filter.OrderByDescending(s => s.CourseName),
                "name_desc" => filter.OrderByDescending(s => s.FullName),
                _ => filter.OrderBy(s => s.FullName),
            };

            return View(filter);

        }

        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> Details(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _mapper.ProjectTo<UserDetailsViewModel>(_userManager.Users).FirstOrDefaultAsync(e => e.Id == id);
            //var role = await _userManager.GetRolesAsync(await _userManager.FindByIdAsync(id));
            //user.Role = role[0] == "Teacher" ? "Lärare" : "Elev";
            var role = await _userManager.IsInRoleAsync(await _userManager.FindByIdAsync(id), "Teacher");
            user.Role = role ? "Lärare" : "Elev";

            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }

        [Authorize(Roles = "Teacher")]
        public IActionResult AddTeacherToCourse(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var model = new AddTeacherToCourseViewModel
            {
                Id = id
            };

            if (model == null)
            {
                return NotFound();
            }
            if (Request.IsAjax())
                return PartialView("AddTeacherToCoursePartialView");
            return View(model);
        }

        // POST: Courses/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to, for 
        // more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [ValidateAjax]
        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> AddTeacherToCourse(int id, AddTeacherToCourseViewModel viewModel)
        {
            if (id != viewModel.Id)
            {
                return NotFound();
            }

            var model = await _userManager.FindByIdAsync(viewModel.TeacherId);


            if (ModelState.IsValid)
            {
                model.CourseId = id;
                try
                {
                    _context.Update(model);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserExists(model.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }

                if (Request.IsAjax())
                {
                    var ajaxModel = new AddTeacherToCourseSuccessViewModel
                    {
                        TeacherId = viewModel.TeacherId
                    };

                    return PartialView("AddTeacherSuccessPartialView", ajaxModel);
                }
                TempData["SuccessText"] = $"{model.FirstName} {model.LastName} är nu kursens lärare";
                return RedirectToAction("Edit", "Courses", new { id = model.CourseId });
            }

            if (Request.IsAjax())
            {
                return PartialView("AddTeacherToCoursePartialView", viewModel);
            }
            TempData["FailText"] = $"Ingen lärare tilldelades till kursen!";
            return RedirectToAction("Edit", "Courses", new { id });
        }

        [Authorize(Roles = "Teacher")]
        [HttpPost]
        [ValidateAjax]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveFromCourse(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var model = await _userManager.FindByIdAsync(id);
            var courseId = model.CourseId;

            if (ModelState.IsValid)
            {

                model.CourseId = null;
                try
                {
                    _context.Update(model);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserExists(model.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }

                if (Request.IsAjax())
                {
                    var ajaxModel = new RemoveTeacherFromCourseSuccessViewModel
                    {
                        CourseId = courseId
                    };

                    return PartialView("RemoveTeacherSuccessPartialView", ajaxModel);
                }

                TempData["SuccessText"] = $"{model.FirstName} {model.LastName} är inte längre kursens lärare";
                return RedirectToAction("Edit", "Courses", new { id = courseId });
            }
            TempData["FailText"] = $"Något gick fel!";

            return RedirectToAction("Edit", "Courses", new { courseId });
        }

        private bool UserExists(string id)
        {
            return _context.Users.Any(e => e.Id == id);
        }

    }
}
