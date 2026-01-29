// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// Sidebar Toggle Logic
$(document).ready(function () {
    $('#sidebar-open-btn').on('click', function () {
        $('#floating-sidebar').addClass('active');
        $('#sidebar-open-btn').hide(); // Hide open button when sidebar is active
    });

    $('#sidebar-toggle').on('click', function () {
        $('#floating-sidebar').removeClass('active');
        $('#sidebar-open-btn').show(); // Show open button when sidebar is closed
    });
});
