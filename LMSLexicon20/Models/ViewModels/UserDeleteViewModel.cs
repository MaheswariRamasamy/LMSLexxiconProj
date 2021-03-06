﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace LMSLexicon20.Models.ViewModels
{
    public class UserDeleteViewModel
    {
        public string Id { get; set; }
        [Display(Name = "Namn")]
        public string FullName { get; set; }
        [Display(Name = "Roll")]
        public string Role { get; set; }
        [Display(Name = "Email")]
        public string Email { get; set; }

        [Display(Name = "Telefonnummer")]
        public string PhoneNumber { get; set; }

        [Display(Name = "Registrerad")]
        [DisplayFormat(DataFormatString = "{0:yyyy-MM-dd}")]
        public DateTime RegDate { get; set; }

        [Display(Name = "Kurs")]
        public Course Course { get; set; }
    }
}
