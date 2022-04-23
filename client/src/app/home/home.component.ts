import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { RegisterComponent } from '../register/register.component';
@Component({
  selector: 'app-home',
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.css'],
})
export class HomeComponent implements OnInit {
  registerMode = false;
  users: any;

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.getUsers();
  }
  registerToggle() {
    this.registerMode = !this.registerMode;
  }
  getUsers()
  {
    this.http.get('https://localhost:7068/api/users').subscribe(users => this.users = users);
  }
}
